using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OperationsCopilot.Domain.Abstractions;
using OperationsCopilot.Domain.Knowledge;
using OperationsCopilot.Infrastructure.Persistence;
using Pgvector;

namespace OperationsCopilot.Infrastructure.Knowledge;

/// <summary>
/// Nearest-neighbour search over <c>document_chunks</c> using pgvector's cosine distance
/// operator <c>&lt;=&gt;</c>.
/// </summary>
/// <remarks>
/// This is deliberately raw SQL rather than LINQ. The <c>&lt;=&gt;</c> operator is what the HNSW
/// index is built on, and writing it out makes the ordering that drives index usage explicit —
/// the ORDER BY must reference the same operator for PostgreSQL to choose the index.
/// </remarks>
public sealed class PgVectorKnowledgeBaseSearch(
    OperationsDbContext dbContext,
    IEmbeddingService embeddingService,
    ILogger<PgVectorKnowledgeBaseSearch> logger) : IKnowledgeBaseSearch
{
    public async Task<IReadOnlyList<KnowledgeSearchResult>> SearchAsync(
        string query,
        int topK,
        double minimumScore,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        var limit = Math.Clamp(topK, 1, 25);
        var embedding = new Vector(await embeddingService.EmbedAsync(query, cancellationToken));

        // Cosine distance is in [0, 2]; for normalized embeddings it lands in [0, 1].
        // Similarity is reported as 1 - distance so that higher always means closer.
        var rows = await dbContext.Database
            .SqlQuery<VectorSearchRow>(
                $"""
                 SELECT   id,
                          source_file,
                          document_title,
                          heading,
                          chunk_index,
                          content,
                          (embedding <=> {embedding}) AS distance
                 FROM     document_chunks
                 ORDER BY embedding <=> {embedding}
                 LIMIT    {limit}
                 """)
            .ToListAsync(cancellationToken);

        var results = rows
            .Select(row => new KnowledgeSearchResult(
                row.Id,
                row.SourceFile,
                row.DocumentTitle,
                row.Heading,
                row.ChunkIndex,
                row.Content,
                Score: 1d - row.Distance))
            .Where(result => result.Score >= minimumScore)
            .ToList();

        logger.LogDebug(
            "Knowledge search returned {Kept} of {Retrieved} chunks above score {MinimumScore}.",
            results.Count,
            rows.Count,
            minimumScore);

        return results;
    }

    /// <summary>
    /// Row shape for the raw vector query. The snake_case naming convention applies to
    /// <c>SqlQuery&lt;T&gt;</c> results too, so the SQL selects the underlying column names
    /// directly and EF maps them onto these properties.
    /// </summary>
    private sealed record VectorSearchRow(
        Guid Id,
        string SourceFile,
        string DocumentTitle,
        string Heading,
        int ChunkIndex,
        string Content,
        double Distance);
}
