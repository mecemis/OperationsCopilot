using Microsoft.EntityFrameworkCore;
using OperationsCopilot.Domain.Abstractions;
using OperationsCopilot.Domain.Catalog;

namespace OperationsCopilot.Infrastructure.Persistence;

/// <summary>
/// EF Core implementation of the three database-backed agent tools. Every query is
/// read-only and projects straight into a domain record, so nothing is tracked.
/// </summary>
public sealed class OperationsRepository(OperationsDbContext dbContext) : IOperationsRepository
{
    /// <summary>Window used for the trailing sales figures on a product detail lookup.</summary>
    private const int ProductDetailSalesWindowDays = 90;

    public async Task<IReadOnlyList<LowStockProduct>> GetLowStockProductsAsync(
        LowStockQuery query,
        CancellationToken cancellationToken = default)
    {
        var items = dbContext.InventoryItems
            .AsNoTracking()
            .Include(i => i.Product)
            .Where(i => !i.Product!.IsDiscontinued);

        if (!string.IsNullOrWhiteSpace(query.WarehouseCode))
        {
            var warehouse = query.WarehouseCode.Trim();
            items = items.Where(i => i.WarehouseCode == warehouse);
        }

        if (!string.IsNullOrWhiteSpace(query.Category))
        {
            var category = query.Category.Trim();
            items = items.Where(i => i.Product!.Category.ToLower() == category.ToLower());
        }

        items = query.ThresholdOverride is { } threshold
            ? items.Where(i => i.QuantityOnHand <= threshold)
            : items.Where(i => i.QuantityOnHand <= i.ReorderThreshold);

        return await items
            // Deepest shortfall first: that is the order a category manager acts in.
            .OrderBy(i => i.QuantityOnHand - i.ReorderThreshold)
            .ThenBy(i => i.Product!.Sku)
            .Take(Math.Clamp(query.Limit, 1, 200))
            .Select(i => new LowStockProduct(
                i.Product!.Sku,
                i.Product.Name,
                i.Product.Category,
                i.WarehouseCode,
                i.QuantityOnHand,
                i.ReorderThreshold,
                i.Product.Supplier,
                i.LastCountedOn))
            .ToListAsync(cancellationToken);
    }

    public async Task<SalesSummary> GetSalesSummaryAsync(
        SalesSummaryQuery query,
        CancellationToken cancellationToken = default)
    {
        var sales = dbContext.Sales
            .AsNoTracking()
            .Where(s => s.SoldOn >= query.From && s.SoldOn <= query.To);

        if (!string.IsNullOrWhiteSpace(query.Category))
        {
            var category = query.Category.Trim();
            sales = sales.Where(s => s.Product!.Category.ToLower() == category.ToLower());
        }

        if (!string.IsNullOrWhiteSpace(query.Region))
        {
            var region = query.Region.Trim();
            sales = sales.Where(s => s.Region.ToLower() == region.ToLower());
        }

        var totals = await sales
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Revenue = g.Sum(s => s.TotalAmount),
                Units = g.Sum(s => s.Quantity),
                Lines = g.Count(),
            })
            .SingleOrDefaultAsync(cancellationToken);

        var lines = await GroupSalesAsync(sales, query, cancellationToken);

        return new SalesSummary(
            query.From,
            query.To,
            totals?.Revenue ?? 0m,
            totals?.Units ?? 0,
            totals?.Lines ?? 0,
            query.GroupBy,
            lines);
    }

    /// <summary>
    /// Groups the filtered sales and returns the top rows.
    /// </summary>
    /// <remarks>
    /// Aggregates are projected into an anonymous type and mapped to
    /// <see cref="SalesSummaryLine"/> afterwards. EF Core cannot translate aggregate functions
    /// into a positional record constructor once the query involves a join to Product, so
    /// building the record in memory is what keeps this a single SQL statement.
    /// </remarks>
    private static async Task<IReadOnlyList<SalesSummaryLine>> GroupSalesAsync(
        IQueryable<Sale> sales,
        SalesSummaryQuery query,
        CancellationToken cancellationToken)
    {
        var limit = Math.Clamp(query.Limit, 1, 100);

        // Month is grouped on the date parts rather than a formatted string: PostgreSQL has no
        // translation for .NET composite formatting, and a chronological sort needs the numbers.
        if (query.GroupBy == SalesGrouping.Month)
        {
            var months = await sales
                .GroupBy(s => new { s.SoldOn.Year, s.SoldOn.Month })
                .Select(g => new
                {
                    g.Key.Year,
                    g.Key.Month,
                    Revenue = g.Sum(s => s.TotalAmount),
                    Units = g.Sum(s => s.Quantity),
                    Lines = g.Count(),
                })
                .OrderBy(row => row.Year)
                .ThenBy(row => row.Month)
                .Take(limit)
                .ToListAsync(cancellationToken);

            return [.. months.Select(m => new SalesSummaryLine(
                $"{m.Year}-{m.Month:D2}", m.Revenue, m.Units, m.Lines))];
        }

        var grouped = query.GroupBy switch
        {
            SalesGrouping.Product => sales.GroupBy(s => s.Product!.Sku + " " + s.Product.Name),
            SalesGrouping.Region => sales.GroupBy(s => s.Region),
            _ => sales.GroupBy(s => s.Product!.Category),
        };

        var rows = await grouped
            .Select(g => new
            {
                Group = g.Key,
                Revenue = g.Sum(s => s.TotalAmount),
                Units = g.Sum(s => s.Quantity),
                Lines = g.Count(),
            })
            .OrderByDescending(row => row.Revenue)
            .Take(limit)
            .ToListAsync(cancellationToken);

        return [.. rows.Select(r => new SalesSummaryLine(r.Group, r.Revenue, r.Units, r.Lines))];
    }

    public async Task<ProductDetails?> GetProductDetailsAsync(
        string skuOrName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(skuOrName))
        {
            return null;
        }

        var needle = skuOrName.Trim();

        var product = await dbContext.Products
            .AsNoTracking()
            .Include(p => p.Inventory)
            .Where(p => p.Sku.ToLower() == needle.ToLower() || p.Name.ToLower() == needle.ToLower())
            // An exact SKU match wins over a name that happens to collide.
            .OrderByDescending(p => p.Sku.ToLower() == needle.ToLower())
            .FirstOrDefaultAsync(cancellationToken);

        // Fall back to a contains match so partial product names still resolve.
        product ??= await dbContext.Products
            .AsNoTracking()
            .Include(p => p.Inventory)
            .Where(p => EF.Functions.ILike(p.Name, $"%{needle}%"))
            .OrderBy(p => p.Name.Length)
            .FirstOrDefaultAsync(cancellationToken);

        if (product is null)
        {
            return null;
        }

        var windowStart = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-ProductDetailSalesWindowDays));

        var recent = await dbContext.Sales
            .AsNoTracking()
            .Where(s => s.ProductId == product.Id && s.SoldOn >= windowStart)
            .GroupBy(_ => 1)
            .Select(g => new { Units = g.Sum(s => s.Quantity), Revenue = g.Sum(s => s.TotalAmount) })
            .SingleOrDefaultAsync(cancellationToken);

        var stock = product.Inventory
            .OrderBy(i => i.WarehouseCode)
            .Select(i => new WarehouseStock(i.WarehouseCode, i.QuantityOnHand, i.ReorderThreshold, i.LastCountedOn))
            .ToList();

        return new ProductDetails(
            product.Sku,
            product.Name,
            product.Category,
            product.Description,
            product.UnitPrice,
            product.Supplier,
            product.IsDiscontinued,
            stock.Sum(s => s.QuantityOnHand),
            stock,
            recent?.Units ?? 0,
            recent?.Revenue ?? 0m);
    }
}
