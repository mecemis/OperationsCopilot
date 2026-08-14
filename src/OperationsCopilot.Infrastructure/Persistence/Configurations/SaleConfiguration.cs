using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OperationsCopilot.Domain.Catalog;

namespace OperationsCopilot.Infrastructure.Persistence.Configurations;

internal sealed class SaleConfiguration : IEntityTypeConfiguration<Sale>
{
    public void Configure(EntityTypeBuilder<Sale> builder)
    {
        builder.HasKey(s => s.Id);

        builder.Property(s => s.Region).HasMaxLength(16).IsRequired();
        builder.Property(s => s.Channel).HasMaxLength(32).IsRequired();
        builder.Property(s => s.UnitPrice).HasPrecision(12, 2);
        builder.Property(s => s.TotalAmount).HasPrecision(14, 2);

        builder.HasOne(s => s.Product)
            .WithMany(p => p.Sales)
            .HasForeignKey(s => s.ProductId)
            .OnDelete(DeleteBehavior.Cascade);

        // Every sales summary is a date-range scan, usually narrowed by product or region.
        builder.HasIndex(s => s.SoldOn);
        builder.HasIndex(s => new { s.SoldOn, s.Region });
        builder.HasIndex(s => new { s.ProductId, s.SoldOn });
    }
}
