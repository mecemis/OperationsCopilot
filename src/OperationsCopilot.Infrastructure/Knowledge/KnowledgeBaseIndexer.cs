using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OperationsCopilot.Domain.Abstractions;
using OperationsCopilot.Domain.Knowledge;
using OperationsCopilot.Infrastructure.Options;
using OperationsCopilot.Infrastructure.Persistence;

namespace OperationsCopilot.Infrastructure.Knowledge;

/// <summary>Outcome of one indexing pass, useful for logging and for asserting in tests.</summary>
public sealed record IndexingResult(int DocumentsProcessed, int ChunksWritten, int ChunksUnchanged)
{
    public int TotalChunks => ChunksWritten + ChunksUnchanged;
}

/// <summary>
/// Builds the pgvector index from the Markdown knowledge base: chunk, embed, store.
/// </summary>
/// <remarks>
/// Indexing is idempotent. Each chunk carries a SHA-256 of its text, and a chunk whose hash is
/// unchanged is left alone — embedding calls are the slow and billable part of this pipeline, so
/// re-running startup indexing after an unrelated deploy costs nothing.
/// </remarks>
public sealed class KnowledgeBaseIndexer(
    OperationsDbContext dbContext,
    IEmbeddingService embeddingService,
    IKnowledgeDocumentSource documentSource,
    IOptions<RagOptions> ragOptions,
    ILogger<KnowledgeBaseIndexer> logger)
{
    /// <summary>Embedding APIs are batched; this keeps a single request comfortably within limits.</summary>
    private const int EmbeddingBatchSize = 32;

    private readonly RagOptions _options = ragOptions.Value;

    public async Task<IndexingResult> IndexAsync(CancellationToken cancellationToken = default)
    {
        var documents = await documentSource.LoadAsync(cancellationToken);
        var chunker = new MarkdownChunker(_options.MaxChunkCharacters, _options.ChunkOverlapCharacters);

        var written = 0;
        var unchanged = 0;

        foreach (var document in documents)
        {
            var chunks = chunker.Chunk(document.Markdown);
            var existing = await dbContext.DocumentChunks
                .Where(c => c.SourceFile == document.FileName)
                .ToDictionaryAsync(c => c.ChunkIndex, cancellationToken);

            var stale = chunks
                .Where(chunk => !existing.TryGetValue(chunk.ChunkIndex, out var stored)
                    || stored.ContentHash != Hash(chunk.Content))
                .ToList();

            unchanged += chunks.Count - stale.Count;

            // The document may have shrunk; drop chunks that no longer exist.
            var liveIndexes = chunks.Select(c => c.ChunkIndex).ToHashSet();
            var orphans = existing.Values.Where(c => !liveIndexes.Contains(c.ChunkIndex)).ToList();
            if (orphans.Count > 0)
            {
                dbContext.DocumentChunks.RemoveRange(orphans);
            }

            written += await EmbedAndStoreAsync(document, stale, existing, cancellationToken);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        var result = new IndexingResult(documents.Count, written, unchanged);
        logger.LogInformation(
            "Indexed knowledge base: {Documents} documents, {Written} chunks embedded, {Unchanged} unchanged.",
            result.DocumentsProcessed,
            result.ChunksWritten,
            result.ChunksUnchanged);

        return result;
    }

    private async Task<int> EmbedAndStoreAsync(
        KnowledgeDocument document,
        IReadOnlyList<MarkdownChunk> stale,
        Dictionary<int, DocumentChunk> existing,
        CancellationToken cancellationToken)
    {
        var written = 0;

        foreach (var batch in stale.Chunk(EmbeddingBatchSize))
        {
            var vectors = await embeddingService.EmbedBatchAsync(
                batch.Select(c => c.EmbeddingInput).ToList(),
                cancellationToken);

            for (var i = 0; i < batch.Length; i++)
            {
                Upsert(document, batch[i], vectors[i], existing);
                written++;
            }
        }

        return written;
    }

    private void Upsert(
        KnowledgeDocument document,
        MarkdownChunk chunk,
        float[] embedding,
        Dictionary<int, DocumentChunk> existing)
    {
        if (existing.TryGetValue(chunk.ChunkIndex, out var stored))
        {
            stored.DocumentTitle = chunk.DocumentTitle;
            stored.Heading = chunk.Heading;
            stored.Content = chunk.Content;
            stored.ContentHash = Hash(chunk.Content);
            stored.Embedding = embedding;
            stored.IndexedAt = DateTimeOffset.UtcNow;
            return;
        }

        dbContext.DocumentChunks.Add(new DocumentChunk
        {
            Id = Guid.CreateVersion7(),
            SourceFile = document.FileName,
            DocumentTitle = chunk.DocumentTitle,
            Heading = chunk.Heading,
            ChunkIndex = chunk.ChunkIndex,
            Content = chunk.Content,
            ContentHash = Hash(chunk.Content),
            Embedding = embedding,
            IndexedAt = DateTimeOffset.UtcNow,
        });
    }

    private static string Hash(string content)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(content)));
}
