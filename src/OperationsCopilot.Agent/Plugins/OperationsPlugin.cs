using System.ComponentModel;
using Microsoft.SemanticKernel;
using OperationsCopilot.Domain.Abstractions;
using OperationsCopilot.Domain.Catalog;

namespace OperationsCopilot.Agent.Plugins;

/// <summary>
/// The database-backed tools. Descriptions here are the only thing the model sees when it
/// decides what to call, so they say what each tool answers and when to prefer it — vague
/// descriptions are the usual cause of a model reaching for the wrong tool.
/// </summary>
public sealed class OperationsPlugin(IOperationsRepository repository, TimeProvider timeProvider)
{
    private const int DefaultSalesWindowDays = 30;

    [KernelFunction(ToolNames.GetLowStockProducts)]
    [Description(
        "Lists products that have reached or fallen below their reorder threshold and need " +
        "replenishing. Use for questions about low stock, running out, what to reorder, or " +
        "which items need a purchase order. Returns current quantity, the threshold, the " +
        "shortfall, the supplier, and when the stock was last counted.")]
    public async Task<string> GetLowStockProductsAsync(
        [Description("Limit to one warehouse: WH-EU-01 (EMEA), WH-NA-01 (AMER) or WH-AP-01 (APAC). Omit for all warehouses.")]
        string? warehouseCode = null,
        [Description("Limit to one product category: Power Tools, Electronics, Safety Equipment, Hand Tools or Consumables. Omit for all categories.")]
        string? category = null,
        [Description("Treat stock at or below this number as low, instead of each warehouse's own reorder threshold. Omit unless the user names a specific number.")]
        int? thresholdOverride = null,
        [Description("Maximum rows to return. Defaults to 25.")]
        int limit = 25,
        CancellationToken cancellationToken = default)
    {
        var results = await repository.GetLowStockProductsAsync(
            new LowStockQuery(warehouseCode, category, thresholdOverride, limit),
            cancellationToken);

        if (results.Count == 0)
        {
            return "No products are currently at or below their reorder threshold for the requested filters.";
        }

        return ToolJson.Serialize(new
        {
            count = results.Count,
            products = results.Select(r => new
            {
                r.Sku,
                r.Name,
                r.Category,
                r.WarehouseCode,
                r.QuantityOnHand,
                r.ReorderThreshold,
                r.ShortfallUnits,
                r.Supplier,
                lastCountedOn = r.LastCountedOn.ToString("yyyy-MM-dd"),
            }),
        });
    }

    [KernelFunction(ToolNames.GetSalesSummary)]
    [Description(
        "Aggregates sales revenue and units over a date range, optionally broken down by " +
        "category, product, region or month. Use for questions about revenue, how much sold, " +
        "best or worst sellers, sales trends, or performance in a period.")]
    public async Task<string> GetSalesSummaryAsync(
        [Description("Number of days back from today to include, for example 30 or 90. Use this for relative periods such as 'last quarter'. Ignored when startDate is supplied.")]
        int? lastDays = null,
        [Description("Start of the range as yyyy-MM-dd. Supply with endDate for an explicit period.")]
        string? startDate = null,
        [Description("End of the range as yyyy-MM-dd. Defaults to today when startDate is supplied.")]
        string? endDate = null,
        [Description("Breakdown dimension: Category, Product, Region or Month. Defaults to Category.")]
        string groupBy = "Category",
        [Description("Limit to one product category. Omit for all categories.")]
        string? category = null,
        [Description("Limit to one sales region: EMEA, AMER or APAC. Omit for all regions.")]
        string? region = null,
        CancellationToken cancellationToken = default)
    {
        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        var (from, to) = ResolveRange(lastDays, startDate, endDate, today);

        if (!Enum.TryParse<SalesGrouping>(groupBy, ignoreCase: true, out var grouping))
        {
            grouping = SalesGrouping.Category;
        }

        var summary = await repository.GetSalesSummaryAsync(
            new SalesSummaryQuery(from, to, category, region, grouping),
            cancellationToken);

        if (summary.OrderLineCount == 0)
        {
            return $"No sales were recorded between {from:yyyy-MM-dd} and {to:yyyy-MM-dd} for the requested filters.";
        }

        return ToolJson.Serialize(new
        {
            from = summary.From.ToString("yyyy-MM-dd"),
            to = summary.To.ToString("yyyy-MM-dd"),
            summary.TotalRevenue,
            summary.TotalUnits,
            summary.OrderLineCount,
            groupedBy = summary.GroupedBy.ToString(),
            breakdown = summary.Lines.Select(l => new { l.Group, l.Revenue, l.Units, l.OrderLineCount }),
        });
    }

    [KernelFunction(ToolNames.GetProductDetails)]
    [Description(
        "Looks up one product by SKU (such as PT-1001) or by name, returning its description, " +
        "price, supplier, stock in every warehouse, and its sales over the last 90 days. Use " +
        "when the user asks about a specific product rather than a group of them.")]
    public async Task<string> GetProductDetailsAsync(
        [Description("The product's SKU, such as PT-1001, or its name, such as 'Torqline 18V Brushless Drill'. A partial name is accepted.")]
        string skuOrName,
        CancellationToken cancellationToken = default)
    {
        var product = await repository.GetProductDetailsAsync(skuOrName, cancellationToken);

        if (product is null)
        {
            return $"No product matches '{skuOrName}'. Ask the user to confirm the SKU or product name.";
        }

        return ToolJson.Serialize(new
        {
            product.Sku,
            product.Name,
            product.Category,
            product.Description,
            product.UnitPrice,
            product.Supplier,
            product.IsDiscontinued,
            product.TotalQuantityOnHand,
            stockByWarehouse = product.StockByWarehouse.Select(s => new
            {
                s.WarehouseCode,
                s.QuantityOnHand,
                s.ReorderThreshold,
                isBelowThreshold = s.QuantityOnHand <= s.ReorderThreshold,
                lastCountedOn = s.LastCountedOn.ToString("yyyy-MM-dd"),
            }),
            product.UnitsSoldLast90Days,
            product.RevenueLast90Days,
        });
    }

    /// <summary>
    /// Turns whichever of the three date arguments the model supplied into a concrete range.
    /// Explicit dates win over a relative window; when neither is given, a sensible default beats
    /// asking the user a clarifying question they did not need.
    /// </summary>
    private static (DateOnly From, DateOnly To) ResolveRange(
        int? lastDays,
        string? startDate,
        string? endDate,
        DateOnly today)
    {
        if (DateOnly.TryParse(startDate, out var parsedStart))
        {
            var parsedEnd = DateOnly.TryParse(endDate, out var e) ? e : today;
            return parsedEnd < parsedStart ? (parsedEnd, parsedStart) : (parsedStart, parsedEnd);
        }

        var days = Math.Clamp(lastDays ?? DefaultSalesWindowDays, 1, 3650);
        return (today.AddDays(-days), today);
    }
}
