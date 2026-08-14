using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using OperationsCopilot.Domain.Knowledge;
using Pgvector;

namespace OperationsCopilot.Infrastructure.Persistence.Configurations;

internal sealed class DocumentChunkConfiguration : IEntityTypeConfiguration<DocumentChunk>
{
    /// <summary>
    /// Bridges the domain's plain <c>float[]</c> to pgvector's <c>Vector</c>. The conversion keeps
    /// the <c>Pgvector</c> package (and its Npgsql dependency) out of the domain project.
    /// </summary>
    private static readonly ValueConverter<float[], Vector> EmbeddingConverter =
        new(clr => new Vector(clr), db => db.ToArray());

    private static readonly ValueComparer<float[]> EmbeddingComparer =
        new(
            (left, right) => left != null && right != null && left.SequenceEqual(right),
            value => value.Aggregate(0, (hash, item) => HashCode.Combine(hash, item.GetHashCode())),
            value => value.ToArray());

    public void Configure(EntityTypeBuilder<DocumentChunk> builder)
    {
        builder.HasKey(c => c.Id);

        builder.Property(c => c.SourceFile).HasMaxLength(260).IsRequired();
        builder.Property(c => c.DocumentTitle).HasMaxLength(300).IsRequired();
        builder.Property(c => c.Heading).HasMaxLength(300).IsRequired();
        builder.Property(c => c.Content).IsRequired();
        builder.Property(c => c.ContentHash).HasMaxLength(64).IsRequired();

        builder.Property(c => c.Embedding)
            .HasColumnType(EmbeddingDefaults.ColumnType)
            .HasConversion(EmbeddingConverter, EmbeddingComparer)
            .IsRequired();

        // Re-indexing replaces a document's chunks wholesale, so lookup is by file then position.
        builder.HasIndex(c => new { c.SourceFile, c.ChunkIndex }).IsUnique();

        // Two things about the embedding column are deliberately not owned by EF Core:
        //
        //  - The HNSW index is created in the migration, because EF cannot express a pgvector
        //    operator class, and the index must name the cosine class explicitly to be used by
        //    the `<=>` searches in PgVectorKnowledgeBaseSearch.
        //  - The column width above is only the value a fresh migration starts at. The real
        //    width depends on the configured embedding model and is reconciled at startup by
        //    VectorSchema.
    }
}
