using OperationsCopilot.Domain.Catalog;

namespace OperationsCopilot.Domain.Abstractions;

/// <summary>
/// Read-only access to operational data. Each method backs exactly one agent tool,
/// which keeps the tool surface and the query surface in step.
/// </summary>
public interface IOperationsRepository
{
    Task<IReadOnlyList<LowStockProduct>> GetLowStockProductsAsync(
        LowStockQuery query,
        CancellationToken cancellationToken = default);

    Task<SalesSummary> GetSalesSummaryAsync(
        SalesSummaryQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>Looks up one product by exact SKU, or by a case-insensitive name match.</summary>
    /// <returns>The product, or null when nothing matches.</returns>
    Task<ProductDetails?> GetProductDetailsAsync(
        string skuOrName,
        CancellationToken cancellationToken = default);
}
