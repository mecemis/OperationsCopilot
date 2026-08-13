namespace OperationsCopilot.Domain.Catalog;

/// <summary>Stock position for one product in one warehouse.</summary>
public class InventoryItem
{
    public int Id { get; set; }

    public int ProductId { get; set; }

    public Product? Product { get; set; }

    /// <summary>Warehouse identifier, e.g. <c>WH-EU-01</c>.</summary>
    public required string WarehouseCode { get; set; }

    public int QuantityOnHand { get; set; }

    /// <summary>Stock level at or below which the product counts as low on stock.</summary>
    public int ReorderThreshold { get; set; }

    public DateOnly LastCountedOn { get; set; }

    /// <summary>True when on-hand stock has reached the reorder point for this warehouse.</summary>
    public bool IsBelowThreshold => QuantityOnHand <= ReorderThreshold;
}
