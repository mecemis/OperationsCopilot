namespace OperationsCopilot.Domain.Knowledge;

/// <summary>One retrieved chunk with its similarity score, ready to be cited.</summary>
/// <param name="Score">
/// Cosine similarity in [0, 1]; higher is closer. Derived from pgvector's cosine
/// distance operator as <c>1 - distance</c>.
/// </param>
public sealed record KnowledgeSearchResult(
    Guid ChunkId,
    string SourceFile,
    string DocumentTitle,
    string Heading,
    int ChunkIndex,
    string Content,
    double Score);
