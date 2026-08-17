namespace OperationsCopilot.EvaluationTests.Rag;

/// <summary>One labelled retrieval case: a question, and the document that should answer it.</summary>
/// <param name="ExpectedSources">
/// Source file names that count as relevant. More than one where a question is genuinely
/// answered by several documents.
/// </param>
/// <param name="MustContain">
/// A distinctive phrase from the passage that ought to be retrieved. Asserting on it catches the
/// case where the right file is found but the wrong section of it comes back — which the
/// file-level metrics alone would score as a success.
/// </param>
public sealed record RetrievalCase(
    string Question,
    string[] ExpectedSources,
    string? MustContain = null)
{
    public IReadOnlySet<string> Relevant => ExpectedSources.ToHashSet(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// The labelled query set the retrieval evaluation runs against.
/// </summary>
/// <remarks>
/// Questions are written the way operations staff actually ask them, not as keyword queries
/// copied out of the documents. Grow this set whenever a real question retrieves badly: a
/// regression that is not in the golden set is a regression nobody notices.
/// </remarks>
public static class RetrievalGoldenSet
{
    public const string InventoryPolicy = "inventory-policy.md";
    public const string SupplierManagement = "supplier-management.md";
    public const string ReturnsAndWarranty = "returns-and-warranty-policy.md";
    public const string PricingAndDiscounts = "pricing-and-discount-policy.md";
    public const string ProductCatalogGuide = "product-catalog-guide.md";

    public static readonly IReadOnlyList<RetrievalCase> Cases =
    [
        new("How is the reorder threshold calculated?",
            [InventoryPolicy], "average daily units sold"),

        new("What should we do when a product goes out of stock?",
            [InventoryPolicy], "stockout"),

        new("How often do we cycle count high value SKUs?",
            [InventoryPolicy], "counted monthly"),

        new("What happens to stock that has not sold in ninety days?",
            [InventoryPolicy], "slow-moving"),

        new("What is the standard lead time for a Tier 2 supplier?",
            [SupplierManagement], "21 days"),

        new("What happens if a supplier's quality acceptance rate drops below 97%?",
            [SupplierManagement], "enhanced inspection"),

        new("How long is the warranty on safety equipment?",
            [ReturnsAndWarranty], "36 months"),

        new("Is there a restocking fee on opened goods that are returned?",
            [ReturnsAndWarranty], "15% restocking fee"),

        new("What is the process for getting an RMA number?",
            [ReturnsAndWarranty], "RMA"),

        new("Who has to approve a 15% discount off list price?",
            [PricingAndDiscounts], "Commercial Finance"),

        new("What is the absolute margin floor for Hand Tools?",
            [PricingAndDiscounts], "33%"),

        new("What volume discount applies to an order of 200 units?",
            [PricingAndDiscounts], "100 to 499 units"),

        new("Which warehouse serves the APAC region?",
            [ProductCatalogGuide], "WH-AP-01"),

        new("What does the SKU prefix SE stand for?",
            [ProductCatalogGuide], "Safety Equipment"),

        // Deliberately spans two documents: discontinued stock is described by the catalog guide
        // and priced by the discount policy. Retrieval should surface at least one of them.
        new("How should we price discontinued stock we are trying to clear?",
            [PricingAndDiscounts, ProductCatalogGuide], "40% off list"),
    ];
}
