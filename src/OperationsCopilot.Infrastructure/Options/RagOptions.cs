using System.ComponentModel.DataAnnotations;

namespace OperationsCopilot.Infrastructure.Options;

/// <summary>Retrieval and chunking settings, bound from the <c>Rag</c> configuration section.</summary>
public sealed class RagOptions
{
    public const string SectionName = "Rag";

    /// <summary>Passages returned per knowledge-base search.</summary>
    [Range(1, 20)]
    public int TopK { get; set; } = 5;

    /// <summary>
    /// Minimum cosine similarity for a passage to be used. Filtering weak matches out here keeps
    /// irrelevant text from reaching the prompt at all.
    /// </summary>
    /// <remarks>
    /// The default is chosen from measurement, not intuition: across the evaluation query set,
    /// genuinely relevant passages score from about 0.22 upwards while off-topic questions peak
    /// around 0.11, so 0.15 separates them with margin on both sides. Set it too high and every
    /// search returns nothing, which reads to the user like an empty knowledge base.
    /// Re-measure with <c>ScoreDistributionTests</c> after changing the embedding model —
    /// this number is specific to one.
    /// </remarks>
    [Range(0d, 1d)]
    public double MinimumScore { get; set; } = 0.15;

    /// <summary>Target chunk size in characters. Chunks split on paragraph boundaries near this size.</summary>
    [Range(200, 4000)]
    public int MaxChunkCharacters { get; set; } = 900;

    /// <summary>Characters of trailing context repeated into the next chunk, to avoid splitting an idea in half.</summary>
    [Range(0, 1000)]
    public int ChunkOverlapCharacters { get; set; } = 150;

    /// <summary>Re-index the knowledge base on startup. Unchanged chunks are skipped by content hash.</summary>
    public bool IndexOnStartup { get; set; } = true;
}
