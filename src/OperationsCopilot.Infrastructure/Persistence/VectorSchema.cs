using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OperationsCopilot.Infrastructure.Ai;

namespace OperationsCopilot.Infrastructure.Persistence;

/// <summary>
/// Keeps every pgvector column's width in step with whatever embedding model is configured.
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
/// lets <see cref="Knowledge.KnowledgeBaseIndexer"/> repopulate the knowledge base. The plan
/// cache is not repopulated and does not need to be: it refills itself from the next few turns,
/// and an empty cache costs latency rather than correctness.
/// </para>
/// <para>
/// It is a no-op when the widths already match, which is every startup except the one right
/// after a provider change.
/// </para>
/// </remarks>
public sealed class VectorSchema(OperationsDbContext dbContext, ILogger<VectorSchema> logger)
{
    /// <summary>Widest vector pgvector will store in a <c>vector</c> column.</summary>
    private const int MaxStorableDimensions = 16000;

    /// <summary>
    /// Widest vector pgvector will build an HNSW index over. Beyond this the column still works,
    /// but searches fall back to a sequential scan.
    /// </summary>
    private const int MaxIndexableDimensions = 2000;

    /// <summary>
    /// Every embedded column in the database. Both are written by the same
    /// <see cref="Domain.Abstractions.IEmbeddingService"/>, so both move together.
    /// </summary>
    private static readonly VectorColumn[] Columns =
    [
        new("document_chunks", "embedding", "ix_document_chunks_embedding_hnsw"),
        new("semantic_plan_cache", "embedding", "ix_semantic_plan_cache_embedding_hnsw"),
    ];

    /// <summary>Aligns every vector column with <paramref name="dimensions"/>, rebuilding indexes that changed.</summary>
    /// <returns>True when any schema was altered.</returns>
    public async Task<bool> EnsureDimensionsAsync(int dimensions, CancellationToken cancellationToken = default)
    {
        // Range-checked here because a column type modifier cannot be a SQL parameter, so the
        // value is formatted into DDL below. An int that has passed this check is safe to embed.
        ArgumentOutOfRangeException.ThrowIfLessThan(dimensions, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(dimensions, MaxStorableDimensions);

        var altered = false;

        foreach (var column in Columns)
        {
            altered |= await EnsureColumnAsync(column, dimensions, cancellationToken);
        }

        return altered;
    }

    private async Task<bool> EnsureColumnAsync(
        VectorColumn column,
        int dimensions,
        CancellationToken cancellationToken)
    {
        var current = await GetCurrentDimensionsAsync(column, cancellationToken);

        if (current == dimensions)
        {
            logger.LogDebug("{Table}.{Column} is already vector({Dimensions}).", column.Table, column.Column, dimensions);
            return false;
        }

        logger.LogWarning(
            "Embedding width of {Table}.{Column} changed from {Current} to {Target}. Rebuilding the " +
            "column and clearing its rows: vectors from different models cannot be compared.",
            column.Table,
            column.Column,
            current?.ToString() ?? "unset",
            dimensions);

        // Order matters. The index depends on the column type so it goes first, and the rows have
        // to go before the type change or the ALTER fails on the width mismatch.
        var sql =
            $"""
             DROP INDEX IF EXISTS {column.IndexName};
             TRUNCATE TABLE {column.Table};
             ALTER TABLE {column.Table} ALTER COLUMN {column.Column} TYPE vector({dimensions});
             """;

        if (dimensions <= MaxIndexableDimensions)
        {
            sql +=
                $"""

                 CREATE INDEX {column.IndexName}
                     ON {column.Table}
                     USING hnsw ({column.Column} vector_cosine_ops)
                     WITH (m = 16, ef_construction = 64);
                 """;
        }
        else
        {
            logger.LogWarning(
                "pgvector cannot build an HNSW index above {Limit} dimensions, so none was created " +
                "for the {Dimensions}-dimensional {Table}.{Column}. Search still works but scans " +
                "the whole table. Consider an embedding model at or below the limit.",
                MaxIndexableDimensions,
                dimensions,
                column.Table,
                column.Column);
        }

#pragma warning disable EF1002 // DDL type modifiers cannot be parameterised; `dimensions` is a range-checked int.
        await dbContext.Database.ExecuteSqlRawAsync(sql, cancellationToken);
#pragma warning restore EF1002

        logger.LogInformation("{Table}.{Column} rebuilt as vector({Dimensions}).", column.Table, column.Column, dimensions);
        return true;
    }

    /// <summary>
    /// Reads the declared width from the catalog. pgvector stores it in <c>atttypmod</c>, which
    /// is the dimension verbatim, or -1 when the column was declared without one.
    /// </summary>
    private async Task<int?> GetCurrentDimensionsAsync(VectorColumn column, CancellationToken cancellationToken)
    {
        var results = await dbContext.Database
            .SqlQuery<int>(
                $"""
                 SELECT a.atttypmod AS "Value"
                 FROM   pg_attribute a
                 JOIN   pg_class c ON c.oid = a.attrelid
                 JOIN   pg_namespace n ON n.oid = c.relnamespace
                 WHERE  c.relname = {column.Table}
                   AND  a.attname = {column.Column}
                   AND  n.nspname = current_schema()
                   AND  a.attnum > 0
                   AND  NOT a.attisdropped
                 """)
            .ToListAsync(cancellationToken);

        return results.Count == 0 || results[0] <= 0 ? null : results[0];
    }

    private sealed record VectorColumn(string Table, string Column, string IndexName);
}
