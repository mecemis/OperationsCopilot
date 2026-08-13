namespace OperationsCopilot.Domain.Catalog;

/// <summary>A sellable item in the catalog. One product has one inventory row per warehouse.</summary>
public class Product
{
    public int Id { get; set; }

    /// <summary>Human-facing stock keeping unit, e.g. <c>NB-4410</c>. Unique.</summary>
    public required string Sku { get; set; }

    public required string Name { get; set; }

    public required string Category { get; set; }

    public required string Description { get; set; }

    public decimal UnitPrice { get; set; }

    public required string Supplier { get; set; }

    public bool IsDiscontinued { get; set; }

    public ICollection<InventoryItem> Inventory { get; set; } = [];

    public ICollection<Sale> Sales { get; set; } = [];
}
