using OperationsCopilot.Domain.Knowledge;

namespace OperationsCopilot.Domain.Abstractions;

/// <summary>Vector search over the indexed company documents.</summary>
public interface IKnowledgeBaseSearch
{
    /// <param name="query">Natural-language query; embedded before searching.</param>
    /// <param name="topK">Maximum passages to return.</param>
    /// <param name="minimumScore">
    /// Drop results below this cosine similarity. Filtering here keeps weak matches
    /// out of the prompt rather than relying on the model to ignore them.
    /// </param>
    Task<IReadOnlyList<KnowledgeSearchResult>> SearchAsync(
        string query,
        int topK,
        double minimumScore,
        CancellationToken cancellationToken = default);
}
