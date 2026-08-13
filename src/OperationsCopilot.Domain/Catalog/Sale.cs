namespace OperationsCopilot.Domain.Catalog;

/// <summary>A single completed sales line. Sales are append-only.</summary>
public class Sale
{
    public long Id { get; set; }

    public int ProductId { get; set; }

    public Product? Product { get; set; }

    public int Quantity { get; set; }

    /// <summary>Price actually charged per unit, which may differ from the catalog price.</summary>
    public decimal UnitPrice { get; set; }

    public decimal TotalAmount { get; set; }

    /// <summary>Sales region, e.g. <c>EMEA</c>.</summary>
    public required string Region { get; set; }

    public required string Channel { get; set; }

    public DateOnly SoldOn { get; set; }
}
