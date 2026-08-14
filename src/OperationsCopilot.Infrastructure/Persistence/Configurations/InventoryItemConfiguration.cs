using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OperationsCopilot.Domain.Catalog;

namespace OperationsCopilot.Infrastructure.Persistence.Configurations;

internal sealed class InventoryItemConfiguration : IEntityTypeConfiguration<InventoryItem>
{
    public void Configure(EntityTypeBuilder<InventoryItem> builder)
    {
        builder.HasKey(i => i.Id);

        builder.Property(i => i.WarehouseCode).HasMaxLength(16).IsRequired();

        builder.Ignore(i => i.IsBelowThreshold);

        builder.HasOne(i => i.Product)
            .WithMany(p => p.Inventory)
            .HasForeignKey(i => i.ProductId)
            .OnDelete(DeleteBehavior.Cascade);

        // One stock row per product per warehouse.
        builder.HasIndex(i => new { i.ProductId, i.WarehouseCode }).IsUnique();

        // Low-stock scans filter by warehouse and compare quantity to threshold.
        builder.HasIndex(i => new { i.WarehouseCode, i.QuantityOnHand });
    }
}
