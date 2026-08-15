using Microsoft.Extensions.AI;
using OperationsCopilot.Domain.Abstractions;

namespace OperationsCopilot.Infrastructure.Embeddings;

/// <summary>
/// Embeds text through a <see cref="IEmbeddingGenerator{TInput,TEmbedding}"/>, whichever provider
/// supplies it.
/// </summary>
/// <remarks>
/// Azure OpenAI and Ollama both arrive here as the same abstraction, so there is one
/// implementation rather than one per provider. The concrete client is chosen in
/// <see cref="Ai.AiClientFactory"/>, at the composition root.
/// </remarks>
public sealed class GeneratedEmbeddingService(
    IEmbeddingGenerator<string, Embedding<float>> generator,
    int dimensions) : IEmbeddingService
{
    public int Dimensions { get; } = dimensions;

    public async Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default)
    {
        var result = await EmbedBatchAsync([text], cancellationToken);
        return result[0];
    }

    public async Task<IReadOnlyList<float[]>> EmbedBatchAsync(
        IReadOnlyList<string> texts,
        CancellationToken cancellationToken = default)
    {
        if (texts.Count == 0)
        {
            return [];
        }

        var embeddings = await generator.GenerateAsync(texts, cancellationToken: cancellationToken);
        var vectors = embeddings.Select(embedding => embedding.Vector.ToArray()).ToList();

        foreach (var vector in vectors)
        {
            Guard(vector.Length);
        }

        return vectors;
    }

    /// <summary>
    /// A width mismatch otherwise surfaces later as an opaque Postgres error on insert. Failing
    /// here names the actual cause: the configured dimension does not match the model.
    /// </summary>
    private void Guard(int actual)
    {
        if (actual == Dimensions)
        {
            return;
        }

        throw new InvalidOperationException(
            $"The embedding model returned {actual}-dimensional vectors but is configured as " +
            $"{Dimensions}. Correct the EmbeddingDimensions setting for the active provider " +
            "(AzureOpenAI:EmbeddingDimensions or Ollama:EmbeddingDimensions) so it matches the " +
            "model — for example nomic-embed-text is 768 and text-embedding-3-small is 1536.");
    }
}
