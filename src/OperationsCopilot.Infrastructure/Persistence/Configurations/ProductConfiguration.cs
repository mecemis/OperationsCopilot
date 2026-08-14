using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OperationsCopilot.Domain.Catalog;

namespace OperationsCopilot.Infrastructure.Persistence.Configurations;

internal sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.HasKey(p => p.Id);

        builder.Property(p => p.Sku).HasMaxLength(32).IsRequired();
        builder.Property(p => p.Name).HasMaxLength(200).IsRequired();
        builder.Property(p => p.Category).HasMaxLength(64).IsRequired();
        builder.Property(p => p.Description).HasMaxLength(2000).IsRequired();
        builder.Property(p => p.Supplier).HasMaxLength(120).IsRequired();
        builder.Property(p => p.UnitPrice).HasPrecision(12, 2);

        builder.HasIndex(p => p.Sku).IsUnique();
        builder.HasIndex(p => p.Category);

        // Product lookup by name is a first-class access path: GetProductDetails accepts a
        // name as well as a SKU, and matches case-insensitively via lower(name).
        builder.HasIndex(p => p.Name);
    }
}
