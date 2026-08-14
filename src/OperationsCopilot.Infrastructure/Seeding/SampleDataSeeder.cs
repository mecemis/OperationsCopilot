using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OperationsCopilot.Domain.Catalog;
using OperationsCopilot.Infrastructure.Persistence;

namespace OperationsCopilot.Infrastructure.Seeding;

/// <summary>
/// Populates the database with a fixed demo dataset: products, per-warehouse stock, and roughly
/// six months of sales history.
/// </summary>
/// <remarks>
/// <para>
/// The generator is seeded with a constant, so every environment gets byte-identical data. That
/// is what makes the evaluation suite meaningful — an expected answer such as "four Power Tools
/// products are below their reorder point" stays true across machines and CI runs.
/// </para>
/// <para>
/// Sales dates are anchored to the current date rather than a hard-coded one, so relative
/// questions ("last 30 days", "this quarter") keep working however long after the repository was
/// cloned the sample is run.
/// </para>
/// </remarks>
public sealed class SampleDataSeeder(OperationsDbContext dbContext, ILogger<SampleDataSeeder> logger)
{
    /// <summary>Fixed seed: reproducible data is worth more here than variety.</summary>
    private const int RandomSeed = 20260101;

    private const int SalesHistoryDays = 180;

    /// <summary>Scales <c>DemandWeight</c> into a sales-line count per product over the whole window.</summary>
    private const int BaseSalesLinesPerProduct = 90;

    public async Task<bool> SeedAsync(CancellationToken cancellationToken = default)
    {
        if (await dbContext.Products.AnyAsync(cancellationToken))
        {
            logger.LogInformation("Sample data already present; skipping seed.");
            return false;
        }

        var random = new Random(RandomSeed);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var products = BuildProducts();
        dbContext.Products.AddRange(products);
        await dbContext.SaveChangesAsync(cancellationToken);

        dbContext.InventoryItems.AddRange(BuildInventory(products, random, today));
        dbContext.Sales.AddRange(BuildSales(products, random, today));
        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Seeded {Products} products, {Inventory} inventory rows and {Sales} sales lines.",
            products.Count,
            await dbContext.InventoryItems.CountAsync(cancellationToken),
            await dbContext.Sales.CountAsync(cancellationToken));

        return true;
    }

    private static List<Product> BuildProducts() =>
    [
        .. SampleCatalog.Products.Select(p => new Product
        {
            Sku = p.Sku,
            Name = p.Name,
            Category = p.Category,
            Description = p.Description,
            UnitPrice = p.UnitPrice,
            Supplier = p.Supplier,
            IsDiscontinued = p.IsDiscontinued,
        }),
    ];

    private static List<InventoryItem> BuildInventory(
        IReadOnlyList<Product> products,
        Random random,
        DateOnly today)
    {
        var items = new List<InventoryItem>();

        foreach (var product in products)
        {
            var definition = SampleCatalog.Products.Single(p => p.Sku == product.Sku);

            foreach (var warehouse in SampleCatalog.Warehouses.Where(w => w.Categories.Contains(product.Category)))
            {
                // Threshold tracks demand, the way the inventory policy says it should:
                // roughly lead-time demand plus safety stock.
                var threshold = Math.Max(10, (int)Math.Round(definition.DemandWeight * 45));

                // About one product in four is deliberately left at or below its reorder point so
                // that GetLowStockProducts has something to find on a fresh database.
                var quantity = random.Next(100) switch
                {
                    < 18 => random.Next(0, threshold + 1),                        // low or critical
                    < 26 => random.Next(threshold + 1, threshold + threshold / 2), // just above
                    _ => random.Next(threshold * 2, threshold * 5),                // healthy
                };

                items.Add(new InventoryItem
                {
                    ProductId = product.Id,
                    WarehouseCode = warehouse.Code,
                    QuantityOnHand = quantity,
                    ReorderThreshold = threshold,
                    LastCountedOn = today.AddDays(-random.Next(1, 95)),
                });
            }
        }

        return items;
    }

    private static List<Sale> BuildSales(IReadOnlyList<Product> products, Random random, DateOnly today)
    {
        var sales = new List<Sale>();
        var windowStart = today.AddDays(-SalesHistoryDays);

        foreach (var product in products)
        {
            var definition = SampleCatalog.Products.Single(p => p.Sku == product.Sku);

            // Discontinued lines still sold earlier in the window; they just stop partway through.
            var lineCount = (int)Math.Round(definition.DemandWeight * BaseSalesLinesPerProduct);
            var lastSellableDay = definition.IsDiscontinued ? SalesHistoryDays / 2 : SalesHistoryDays;

            var regions = SampleCatalog.Warehouses
                .Where(w => w.Categories.Contains(product.Category))
                .Select(w => w.Region)
                .ToList();

            for (var i = 0; i < lineCount; i++)
            {
                var soldOn = windowStart.AddDays(random.Next(0, lastSellableDay + 1));
                var quantity = WeightedQuantity(random);

                // Realised price sits within the policy's discount range rather than at list.
                var discount = random.Next(100) switch
                {
                    < 55 => 0m,
                    < 85 => 0.03m,
                    < 96 => 0.07m,
                    _ => 0.12m,
                };

                var unitPrice = decimal.Round(product.UnitPrice * (1m - discount), 2);

                sales.Add(new Sale
                {
                    ProductId = product.Id,
                    Quantity = quantity,
                    UnitPrice = unitPrice,
                    TotalAmount = decimal.Round(unitPrice * quantity, 2),
                    Region = regions[random.Next(regions.Count)],
                    Channel = SampleCatalog.Channels[random.Next(SampleCatalog.Channels.Count)],
                    SoldOn = soldOn,
                });
            }
        }

        return sales;
    }

    /// <summary>Most order lines are small; a few are bulk. Uniform quantities make every summary look alike.</summary>
    private static int WeightedQuantity(Random random) => random.Next(100) switch
    {
        < 50 => random.Next(1, 6),
        < 80 => random.Next(6, 25),
        < 95 => random.Next(25, 100),
        _ => random.Next(100, 500),
    };
}
