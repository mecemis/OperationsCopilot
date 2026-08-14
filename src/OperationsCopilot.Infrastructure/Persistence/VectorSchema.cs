using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OperationsCopilot.Infrastructure.Ai;

namespace OperationsCopilot.Infrastructure.Persistence;

/// <summary>
/// Keeps the <c>document_chunks.embedding</c> column's width in step with whatever embedding
/// model is configured.
/// </summary>
/// <remarks>
/// <para>
/// A pgvector column has a fixed dimension, and different embedding models produce different
/// widths — text-embedding-3-small is 1536, nomic-embed-text is 768. Switching providers
/// therefore needs a schema change that a static EF migration cannot express, because the width
/// is only known from configuration at run time.
/// </para>
/// <para>
/// Changing it also discards every stored vector, and that is not a compromise: embeddings from
/// two different models are not comparable, so a mixed index returns nonsense. Re-indexing after
/// a provider switch is mandatory however the schema is managed, so this drops the old rows and
/// lets <see cref="Knowledge.KnowledgeBaseIndexer"/> repopulate them.
/// </para>
/// <para>
/// It is a no-op when the width already matches, which is every startup except the one right
/// after a provider change.
/// </para>
/// </remarks>
public sealed class VectorSchema(OperationsDbContext dbContext, ILogger<VectorSchema> logger)
{
    private const string Table = "document_chunks";
    private const string Column = "embedding";
    private const string IndexName = "ix_document_chunks_embedding_hnsw";

    /// <summary>Widest vector pgvector will store in a <c>vector</c> column.</summary>
    private const int MaxStorableDimensions = 16000;

    /// <summary>
    /// Widest vector pgvector will build an HNSW index over. Beyond this the column still works,
    /// but searches fall back to a sequential scan.
    /// </summary>
    private const int MaxIndexableDimensions = 2000;

    /// <summary>Aligns the column with <paramref name="dimensions"/>, rebuilding the index if it changed.</summary>
    /// <returns>True when the schema was altered.</returns>
    public async Task<bool> EnsureDimensionsAsync(int dimensions, CancellationToken cancellationToken = default)
    {
        // Range-checked here because a column type modifier cannot be a SQL parameter, so the
        // value is formatted into DDL below. An int that has passed this check is safe to embed.
        ArgumentOutOfRangeException.ThrowIfLessThan(dimensions, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(dimensions, MaxStorableDimensions);

        var current = await GetCurrentDimensionsAsync(cancellationToken);

        if (current == dimensions)
        {
            logger.LogDebug("Embedding column is already vector({Dimensions}).", dimensions);
            return false;
        }

        logger.LogWarning(
            "Embedding width changed from {Current} to {Target}. Rebuilding the vector column and " +
            "clearing the knowledge base: vectors from different models cannot be compared, so it " +
            "will be re-indexed from source.",
            current?.ToString() ?? "unset",
            dimensions);

        // Order matters. The index depends on the column type so it goes first, and the rows have
        // to go before the type change or the ALTER fails on the width mismatch.
        var sql =
            $"""
             DROP INDEX IF EXISTS {IndexName};
             TRUNCATE TABLE {Table};
             ALTER TABLE {Table} ALTER COLUMN {Column} TYPE vector({dimensions});
             """;

        if (dimensions <= MaxIndexableDimensions)
        {
            sql +=
                $"""

                 CREATE INDEX {IndexName}
                     ON {Table}
                     USING hnsw ({Column} vector_cosine_ops)
                     WITH (m = 16, ef_construction = 64);
                 """;
        }
        else
        {
            logger.LogWarning(
                "pgvector cannot build an HNSW index above {Limit} dimensions, so none was created " +
                "for the {Dimensions}-dimensional column. Search still works but scans the whole " +
                "table. Consider an embedding model at or below the limit.",
                MaxIndexableDimensions,
                dimensions);
        }

#pragma warning disable EF1002 // DDL type modifiers cannot be parameterised; `dimensions` is a range-checked int.
        await dbContext.Database.ExecuteSqlRawAsync(sql, cancellationToken);
#pragma warning restore EF1002

        logger.LogInformation("Embedding column rebuilt as vector({Dimensions}).", dimensions);
        return true;
    }

    /// <summary>
    /// Reads the declared width from the catalog. pgvector stores it in <c>atttypmod</c>, which
    /// is the dimension verbatim, or -1 when the column was declared without one.
    /// </summary>
    private async Task<int?> GetCurrentDimensionsAsync(CancellationToken cancellationToken)
    {
        var results = await dbContext.Database
            .SqlQuery<int>(
                $"""
                 SELECT a.atttypmod AS "Value"
                 FROM   pg_attribute a
                 JOIN   pg_class c ON c.oid = a.attrelid
                 JOIN   pg_namespace n ON n.oid = c.relnamespace
                 WHERE  c.relname = {Table}
                   AND  a.attname = {Column}
                   AND  n.nspname = current_schema()
                   AND  a.attnum > 0
                   AND  NOT a.attisdropped
                 """)
            .ToListAsync(cancellationToken);

        return results.Count == 0 || results[0] <= 0 ? null : results[0];
    }
}
