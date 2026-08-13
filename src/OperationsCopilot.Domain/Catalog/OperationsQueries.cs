namespace OperationsCopilot.Domain.Catalog;

/// <summary>Filter for the <c>GetLowStockProducts</c> tool.</summary>
/// <param name="WarehouseCode">Restrict to one warehouse. Null means all warehouses.</param>
/// <param name="Category">Restrict to one product category. Null means all categories.</param>
/// <param name="ThresholdOverride">
/// Treat stock at or below this number as low, ignoring each warehouse's configured
/// reorder threshold. Null uses the per-warehouse threshold.
/// </param>
/// <param name="Limit">Maximum rows to return.</param>
public sealed record LowStockQuery(
    string? WarehouseCode = null,
    string? Category = null,
    int? ThresholdOverride = null,
    int Limit = 25);

/// <summary>One product that has reached its reorder point.</summary>
public sealed record LowStockProduct(
    string Sku,
    string Name,
    string Category,
    string WarehouseCode,
    int QuantityOnHand,
    int ReorderThreshold,
    string Supplier,
    DateOnly LastCountedOn)
{
    /// <summary>Units to order to return to the reorder threshold. Zero when already at it.</summary>
    public int ShortfallUnits => Math.Max(0, ReorderThreshold - QuantityOnHand);
}

/// <summary>Filter for the <c>GetSalesSummary</c> tool.</summary>
public sealed record SalesSummaryQuery(
    DateOnly From,
    DateOnly To,
    string? Category = null,
    string? Region = null,
    SalesGrouping GroupBy = SalesGrouping.Category,
    int Limit = 15);

/// <summary>Dimension the sales summary is grouped by.</summary>
public enum SalesGrouping
{
    Category,
    Product,
    Region,
    Month,
}

/// <summary>Aggregated sales for one period, plus a per-group breakdown.</summary>
public sealed record SalesSummary(
    DateOnly From,
    DateOnly To,
    decimal TotalRevenue,
    int TotalUnits,
    int OrderLineCount,
    SalesGrouping GroupedBy,
    IReadOnlyList<SalesSummaryLine> Lines);

/// <summary>One row of a grouped sales summary.</summary>
public sealed record SalesSummaryLine(
    string Group,
    decimal Revenue,
    int Units,
    int OrderLineCount);

/// <summary>Full detail for one product, as returned by the <c>GetProductDetails</c> tool.</summary>
public sealed record ProductDetails(
    string Sku,
    string Name,
    string Category,
    string Description,
    decimal UnitPrice,
    string Supplier,
    bool IsDiscontinued,
    int TotalQuantityOnHand,
    IReadOnlyList<WarehouseStock> StockByWarehouse,
    int UnitsSoldLast90Days,
    decimal RevenueLast90Days);

/// <summary>Stock for one product in one warehouse.</summary>
public sealed record WarehouseStock(
    string WarehouseCode,
    int QuantityOnHand,
    int ReorderThreshold,
    DateOnly LastCountedOn);
