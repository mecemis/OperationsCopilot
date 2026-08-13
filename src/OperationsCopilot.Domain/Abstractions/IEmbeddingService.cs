namespace OperationsCopilot.Domain.Abstractions;

/// <summary>Turns text into vectors for indexing and for querying.</summary>
public interface IEmbeddingService
{
    /// <summary>Dimension of every vector this service produces. Must match the pgvector column.</summary>
    int Dimensions { get; }

    Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default);

    /// <summary>Embeds a batch in one round trip. Results are positionally aligned with the input.</summary>
    Task<IReadOnlyList<float[]>> EmbedBatchAsync(
        IReadOnlyList<string> texts,
        CancellationToken cancellationToken = default);
}
