using Microsoft.EntityFrameworkCore;
using OperationsCopilot.Domain.Catalog;
using OperationsCopilot.Domain.Knowledge;

namespace OperationsCopilot.Infrastructure.Persistence;

/// <summary>
/// The single database context: operational tables plus the pgvector-backed document index.
/// Keeping both in one context lets a single query join business data and retrieved passages
/// when that is useful, and keeps the sample easy to follow.
/// </summary>
public sealed class OperationsDbContext(DbContextOptions<OperationsDbContext> options)
    : DbContext(options)
{
    public DbSet<Product> Products => Set<Product>();

    public DbSet<InventoryItem> InventoryItems => Set<InventoryItem>();

    public DbSet<Sale> Sales => Set<Sale>();

    public DbSet<DocumentChunk> DocumentChunks => Set<DocumentChunk>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresExtension("vector");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(OperationsDbContext).Assembly);
    }
}
