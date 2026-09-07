using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using OperationsCopilot.Domain.Planning;
using OperationsCopilot.Infrastructure.Planning;
using Pgvector;

namespace OperationsCopilot.Infrastructure.Persistence.Configurations;

internal sealed class CachedToolPlanConfiguration : IEntityTypeConfiguration<CachedToolPlan>
{
    /// <summary>Same bridge as <see cref="DocumentChunkConfiguration"/>: domain keeps <c>float[]</c>.</summary>
    private static readonly ValueConverter<float[], Vector> EmbeddingConverter =
        new(clr => new Vector(clr), db => db.ToArray());

    private static readonly ValueComparer<float[]> EmbeddingComparer =
        new(
            (left, right) => left != null && right != null && left.SequenceEqual(right),
            value => value.Aggregate(0, (hash, item) => HashCode.Combine(hash, item.GetHashCode())),
            value => value.ToArray());

    /// <summary>
    /// The plan is stored as <c>jsonb</c> rather than as child rows.
    /// </summary>
    /// <remarks>
    /// It is only ever read and written whole — a plan is executed in order or not at all — so a
    /// child table would buy queryability nobody needs and cost a join on the hot path.
    /// </remarks>
    private static readonly ValueConverter<IReadOnlyList<PlannedToolCall>, string> CallsConverter =
        new(clr => ToolPlanSerializer.Serialize(clr), db => ToolPlanSerializer.Deserialize(db));

    private static readonly ValueComparer<IReadOnlyList<PlannedToolCall>> CallsComparer =
        new(
            (left, right) => ToolPlanSerializer.Serialize(left!) == ToolPlanSerializer.Serialize(right!),
            value => ToolPlanSerializer.Serialize(value).GetHashCode(StringComparison.Ordinal),
            value => ToolPlanSerializer.Deserialize(ToolPlanSerializer.Serialize(value)));

    public void Configure(EntityTypeBuilder<CachedToolPlan> builder)
    {
        builder.ToTable("semantic_plan_cache");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.Question).HasMaxLength(2000).IsRequired();

        builder.Property(p => p.Calls)
            .HasColumnType("jsonb")
            .HasConversion(CallsConverter, CallsComparer)
            .IsRequired();

        builder.Property(p => p.Embedding)
            .HasColumnType(EmbeddingDefaults.ColumnType)
            .HasConversion(EmbeddingConverter, EmbeddingComparer)
            .IsRequired();

        // Lookup filters on expiry and on the day-anchor rule before it ever looks at distance,
        // so both columns are worth an index of their own at any realistic cache size.
        builder.HasIndex(p => p.CreatedAt);
        builder.HasIndex(p => new { p.IsDateAnchored, p.CapturedOn });

        // As with document_chunks, the HNSW index and the column's real width are not owned by
        // EF Core: the index needs an operator class EF cannot express, and the width follows the
        // configured embedding model rather than the migration. VectorSchema reconciles both.
    }
}
