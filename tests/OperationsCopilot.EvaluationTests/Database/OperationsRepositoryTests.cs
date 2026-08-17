using Microsoft.Extensions.DependencyInjection;
using OperationsCopilot.Domain.Abstractions;
using OperationsCopilot.Domain.Catalog;
using OperationsCopilot.TestSupport;
using Shouldly;
using Xunit;

namespace OperationsCopilot.EvaluationTests.Database;

/// <summary>
/// Exercises the three database-backed tools against real PostgreSQL and the seeded dataset.
/// </summary>
/// <remarks>
/// These queries use PostgreSQL-specific translation — <c>ILIKE</c>, date grouping, aggregate
/// projections over a join — none of which an in-memory provider reproduces. Running against the
/// real engine is the only way for these to mean anything.
/// </remarks>
[Collection(DatabaseCollection.Name)]
public class OperationsRepositoryTests(SeededDatabaseFixture fixture)
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    [Fact]
    public async Task GetLowStockProductsAsync_ReturnsOnlyProductsAtOrBelowTheirThreshold()
    {
        var results = await QueryLowStockAsync(new LowStockQuery());

        results.ShouldNotBeEmpty("The seed data should always leave some products below threshold.");
        results.ShouldAllBe(r => r.QuantityOnHand <= r.ReorderThreshold);
    }

    [Fact]
    public async Task GetLowStockProductsAsync_OrdersByDeepestShortfallFirst()
    {
        var results = await QueryLowStockAsync(new LowStockQuery());

        // A category manager works the worst shortfall first, so that is the order to return.
        results.Select(r => r.QuantityOnHand - r.ReorderThreshold).ShouldBeInOrder();
    }

    [Fact]
    public async Task GetLowStockProductsAsync_ExcludesDiscontinuedProducts()
    {
        var results = await QueryLowStockAsync(new LowStockQuery(Limit: 200));

        // Discontinued lines are never replenished, so listing them as needing a purchase order
        // would send someone to raise one that policy forbids.
        results.ShouldAllBe(r => r.Sku != "PT-1006");
    }

    [Fact]
    public async Task GetLowStockProductsAsync_FiltersByWarehouse()
    {
        var results = await QueryLowStockAsync(new LowStockQuery(WarehouseCode: "WH-EU-01"));

        results.ShouldAllBe(r => r.WarehouseCode == "WH-EU-01");
    }

    [Fact]
    public async Task GetLowStockProductsAsync_MatchesCategoryCaseInsensitively()
    {
        var results = await QueryLowStockAsync(new LowStockQuery(Category: "power tools", Limit: 200));

        results.ShouldAllBe(r => r.Category == "Power Tools");
    }

    [Fact]
    public async Task GetLowStockProductsAsync_HonoursAThresholdOverride()
    {
        var results = await QueryLowStockAsync(new LowStockQuery(ThresholdOverride: 5, Limit: 200));

        results.ShouldAllBe(r => r.QuantityOnHand <= 5);
    }

    [Fact]
    public async Task GetLowStockProductsAsync_CapsAnAbsurdLimit()
    {
        var results = await QueryLowStockAsync(new LowStockQuery(Limit: 100_000));

        results.Count.ShouldBeLessThanOrEqualTo(200);
    }

    [Fact]
    public async Task GetLowStockProductsAsync_ReportsShortfallAsUnitsNeeded()
    {
        var result = (await QueryLowStockAsync(new LowStockQuery())).First();

        result.ShortfallUnits.ShouldBe(Math.Max(0, result.ReorderThreshold - result.QuantityOnHand));
    }

    [Fact]
    public async Task GetSalesSummaryAsync_TotalsMatchTheSumOfTheBreakdown()
    {
        var summary = await QuerySalesAsync(new SalesSummaryQuery(Today.AddDays(-180), Today));

        summary.OrderLineCount.ShouldBeGreaterThan(0);
        summary.Lines.Sum(l => l.Revenue).ShouldBe(summary.TotalRevenue, tolerance: 0.01m);
        summary.Lines.Sum(l => l.Units).ShouldBe(summary.TotalUnits);
    }

    [Fact]
    public async Task GetSalesSummaryAsync_ReturnsAnEmptySummaryForAPeriodWithNoSales()
    {
        var summary = await QuerySalesAsync(new SalesSummaryQuery(
            new DateOnly(2000, 1, 1),
            new DateOnly(2000, 12, 31)));

        // Zeroes, not an exception: "no sales in that period" is a real answer.
        summary.TotalRevenue.ShouldBe(0m);
        summary.TotalUnits.ShouldBe(0);
        summary.OrderLineCount.ShouldBe(0);
        summary.Lines.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(SalesGrouping.Category)]
    [InlineData(SalesGrouping.Product)]
    [InlineData(SalesGrouping.Region)]
    public async Task GetSalesSummaryAsync_RanksGroupsByRevenue(SalesGrouping grouping)
    {
        var summary = await QuerySalesAsync(new SalesSummaryQuery(
            Today.AddDays(-180), Today, GroupBy: grouping));

        summary.Lines.ShouldNotBeEmpty();
        summary.Lines.Select(l => l.Revenue).ShouldBeInOrder(SortDirection.Descending);
    }

    [Fact]
    public async Task GetSalesSummaryAsync_ReturnsMonthsInChronologicalOrder()
    {
        var summary = await QuerySalesAsync(new SalesSummaryQuery(
            Today.AddDays(-180), Today, GroupBy: SalesGrouping.Month, Limit: 12));

        summary.Lines.Select(l => l.Group).ShouldBeInOrder();
        summary.Lines.ShouldAllBe(l => l.Group.Length == 7 && l.Group[4] == '-');
    }

    [Fact]
    public async Task GetSalesSummaryAsync_FiltersByRegion()
    {
        var all = await QuerySalesAsync(new SalesSummaryQuery(Today.AddDays(-180), Today));
        var emea = await QuerySalesAsync(new SalesSummaryQuery(Today.AddDays(-180), Today, Region: "EMEA"));

        emea.TotalRevenue.ShouldBeGreaterThan(0m);
        emea.TotalRevenue.ShouldBeLessThan(all.TotalRevenue);
    }

    [Fact]
    public async Task GetProductDetailsAsync_FindsAProductBySku()
    {
        var product = await QueryProductAsync("PT-1001");

        product.ShouldNotBeNull();
        product.Sku.ShouldBe("PT-1001");
        product.Name.ShouldBe("Torqline 18V Brushless Drill");
    }

    [Fact]
    public async Task GetProductDetailsAsync_FindsAProductBySkuRegardlessOfCase()
    {
        var product = await QueryProductAsync("pt-1001");

        product.ShouldNotBeNull().Sku.ShouldBe("PT-1001");
    }

    [Fact]
    public async Task GetProductDetailsAsync_FindsAProductByExactName()
    {
        var product = await QueryProductAsync("Guardline Hard Hat Vented");

        product.ShouldNotBeNull().Sku.ShouldBe("SE-3002");
    }

    [Fact]
    public async Task GetProductDetailsAsync_FallsBackToAPartialNameMatch()
    {
        // Users, and models, rarely type the full catalog name.
        var product = await QueryProductAsync("thermal camera");

        product.ShouldNotBeNull().Sku.ShouldBe("EL-2002");
    }

    [Fact]
    public async Task GetProductDetailsAsync_ReturnsNullForAnUnknownProduct()
    {
        (await QueryProductAsync("ZZ-0000")).ShouldBeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task GetProductDetailsAsync_ReturnsNullForAnEmptyQuery(string query)
    {
        (await QueryProductAsync(query)).ShouldBeNull();
    }

    [Fact]
    public async Task GetProductDetailsAsync_ReturnsStockForEveryWarehouseThatCarriesIt()
    {
        var product = await QueryProductAsync("EL-2002");

        // Electronics live in Rotterdam and Singapore, per the product catalog guide.
        product.ShouldNotBeNull().StockByWarehouse
            .Select(s => s.WarehouseCode)
            .ShouldBe(["WH-AP-01", "WH-EU-01"], ignoreOrder: true);

        product.TotalQuantityOnHand.ShouldBe(product.StockByWarehouse.Sum(s => s.QuantityOnHand));
    }

    [Fact]
    public async Task GetProductDetailsAsync_ReportsRecentSalesActivity()
    {
        var product = await QueryProductAsync("SE-3003");

        product.ShouldNotBeNull();
        product.UnitsSoldLast90Days.ShouldBeGreaterThan(0);
        product.RevenueLast90Days.ShouldBeGreaterThan(0m);
    }

    [Fact]
    public async Task GetProductDetailsAsync_MarksADiscontinuedProduct()
    {
        var product = await QueryProductAsync("PT-1006");

        product.ShouldNotBeNull().IsDiscontinued.ShouldBeTrue();
    }

    private async Task<IReadOnlyList<LowStockProduct>> QueryLowStockAsync(LowStockQuery query)
    {
        await using var scope = fixture.CreateScope();

        return await scope.ServiceProvider.GetRequiredService<IOperationsRepository>()
            .GetLowStockProductsAsync(query, TestContext.Current.CancellationToken);
    }

    private async Task<SalesSummary> QuerySalesAsync(SalesSummaryQuery query)
    {
        await using var scope = fixture.CreateScope();

        return await scope.ServiceProvider.GetRequiredService<IOperationsRepository>()
            .GetSalesSummaryAsync(query, TestContext.Current.CancellationToken);
    }

    private async Task<ProductDetails?> QueryProductAsync(string skuOrName)
    {
        await using var scope = fixture.CreateScope();

        return await scope.ServiceProvider.GetRequiredService<IOperationsRepository>()
            .GetProductDetailsAsync(skuOrName, TestContext.Current.CancellationToken);
    }
}
