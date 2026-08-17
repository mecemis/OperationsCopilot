using System.Text.Json;
using Microsoft.Extensions.Time.Testing;
using OperationsCopilot.Agent.Plugins;
using OperationsCopilot.Domain.Abstractions;
using OperationsCopilot.Domain.Catalog;
using Shouldly;
using Xunit;

namespace OperationsCopilot.UnitTests.Agent;

/// <summary>
/// Covers what the plugin adds on top of the repository: date resolution, argument coercion, and
/// the shape of what the model is handed. The repository itself is exercised against a real
/// database in the evaluation suite.
/// </summary>
public class OperationsPluginTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-06-15T12:00:00Z");

    private readonly FakeTimeProvider _time = new(Now);
    private readonly RecordingRepository _repository = new();

    private OperationsPlugin CreatePlugin() => new(_repository, _time);

    [Fact]
    public async Task GetSalesSummary_DefaultsToTheLastThirtyDays()
    {
        await CreatePlugin().GetSalesSummaryAsync(cancellationToken: TestContext.Current.CancellationToken);

        _repository.LastSalesQuery!.From.ShouldBe(new DateOnly(2026, 5, 16));
        _repository.LastSalesQuery.To.ShouldBe(new DateOnly(2026, 6, 15));
    }

    [Fact]
    public async Task GetSalesSummary_ResolvesARelativeWindow()
    {
        await CreatePlugin().GetSalesSummaryAsync(
            lastDays: 90,
            cancellationToken: TestContext.Current.CancellationToken);

        _repository.LastSalesQuery!.From.ShouldBe(new DateOnly(2026, 3, 17));
    }

    [Fact]
    public async Task GetSalesSummary_PrefersExplicitDatesOverARelativeWindow()
    {
        await CreatePlugin().GetSalesSummaryAsync(
            lastDays: 7,
            startDate: "2026-01-01",
            endDate: "2026-02-01",
            cancellationToken: TestContext.Current.CancellationToken);

        _repository.LastSalesQuery!.From.ShouldBe(new DateOnly(2026, 1, 1));
        _repository.LastSalesQuery.To.ShouldBe(new DateOnly(2026, 2, 1));
    }

    [Fact]
    public async Task GetSalesSummary_SwapsAnInvertedRange()
    {
        // Models do get the argument order wrong; a swap is friendlier than an empty result.
        await CreatePlugin().GetSalesSummaryAsync(
            startDate: "2026-02-01",
            endDate: "2026-01-01",
            cancellationToken: TestContext.Current.CancellationToken);

        _repository.LastSalesQuery!.From.ShouldBe(new DateOnly(2026, 1, 1));
        _repository.LastSalesQuery.To.ShouldBe(new DateOnly(2026, 2, 1));
    }

    [Theory]
    [InlineData("region", SalesGrouping.Region)]
    [InlineData("Product", SalesGrouping.Product)]
    [InlineData("MONTH", SalesGrouping.Month)]
    [InlineData("nonsense", SalesGrouping.Category)]
    public async Task GetSalesSummary_ParsesGroupByLeniently(string groupBy, SalesGrouping expected)
    {
        await CreatePlugin().GetSalesSummaryAsync(
            groupBy: groupBy,
            cancellationToken: TestContext.Current.CancellationToken);

        _repository.LastSalesQuery!.GroupBy.ShouldBe(expected);
    }

    [Fact]
    public async Task GetLowStockProducts_TellsTheModelPlainlyWhenNothingIsLow()
    {
        _repository.LowStock = [];

        var result = await CreatePlugin().GetLowStockProductsAsync(
            cancellationToken: TestContext.Current.CancellationToken);

        // A sentence beats a bare "[]": the model should say "nothing is low", not go looking
        // for another tool to try.
        result.ShouldContain("No products");
        result.ShouldNotStartWith("{");
    }

    [Fact]
    public async Task GetLowStockProducts_ReportsTheShortfallForEachRow()
    {
        _repository.LowStock =
        [
            new LowStockProduct("PT-1001", "Drill", "Power Tools", "WH-EU-01", 12, 45, "Torqline", new DateOnly(2026, 6, 1)),
        ];

        var json = JsonDocument.Parse(await CreatePlugin().GetLowStockProductsAsync(
            cancellationToken: TestContext.Current.CancellationToken));

        var product = json.RootElement.GetProperty("products")[0];

        product.GetProperty("sku").GetString().ShouldBe("PT-1001");
        // 45 - 12: the model should not have to do this arithmetic itself.
        product.GetProperty("shortfallUnits").GetInt32().ShouldBe(33);
    }

    [Fact]
    public async Task GetProductDetails_ExplainsAMissWithoutInventingAProduct()
    {
        _repository.ProductDetails = null;

        var result = await CreatePlugin().GetProductDetailsAsync(
            "ZZ-9999",
            TestContext.Current.CancellationToken);

        result.ShouldContain("No product matches 'ZZ-9999'");
    }

    [Fact]
    public async Task GetProductDetails_FlagsWarehousesThatAreBelowThreshold()
    {
        _repository.ProductDetails = new ProductDetails(
            "SE-3001", "Harness", "Safety Equipment", "Fall arrest harness.", 164m, "Guardline", false,
            30,
            [
                new WarehouseStock("WH-EU-01", 10, 25, new DateOnly(2026, 6, 1)),
                new WarehouseStock("WH-AP-01", 20, 15, new DateOnly(2026, 6, 2)),
            ],
            120, 19680m);

        var json = JsonDocument.Parse(await CreatePlugin().GetProductDetailsAsync(
            "SE-3001",
            TestContext.Current.CancellationToken));

        var stock = json.RootElement.GetProperty("stockByWarehouse");

        stock[0].GetProperty("isBelowThreshold").GetBoolean().ShouldBeTrue();
        stock[1].GetProperty("isBelowThreshold").GetBoolean().ShouldBeFalse();
    }

    /// <summary>Captures the query it was asked for and returns whatever the test set up.</summary>
    private sealed class RecordingRepository : IOperationsRepository
    {
        public SalesSummaryQuery? LastSalesQuery { get; private set; }

        public LowStockQuery? LastLowStockQuery { get; private set; }

        public IReadOnlyList<LowStockProduct> LowStock { get; set; } = [];

        public ProductDetails? ProductDetails { get; set; }

        public Task<IReadOnlyList<LowStockProduct>> GetLowStockProductsAsync(
            LowStockQuery query,
            CancellationToken cancellationToken = default)
        {
            LastLowStockQuery = query;
            return Task.FromResult(LowStock);
        }

        public Task<SalesSummary> GetSalesSummaryAsync(
            SalesSummaryQuery query,
            CancellationToken cancellationToken = default)
        {
            LastSalesQuery = query;

            return Task.FromResult(new SalesSummary(
                query.From, query.To, 1000m, 10, 2, query.GroupBy,
                [new SalesSummaryLine("Power Tools", 1000m, 10, 2)]));
        }

        public Task<ProductDetails?> GetProductDetailsAsync(
            string skuOrName,
            CancellationToken cancellationToken = default)
            => Task.FromResult(ProductDetails);
    }
}
