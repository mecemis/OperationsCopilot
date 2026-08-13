namespace OperationsCopilot.Domain.Knowledge;

/// <summary>
/// One embedded slice of a company document. Chunks are the unit of retrieval for RAG:
/// the vector index lives on <see cref="Embedding"/> and citations are built from
/// <see cref="SourceFile"/> plus <see cref="Heading"/>.
/// </summary>
public class DocumentChunk
{
    public Guid Id { get; set; }

    /// <summary>Source Markdown file name, e.g. <c>inventory-policy.md</c>.</summary>
    public required string SourceFile { get; set; }

    /// <summary>Document title taken from the file's top-level heading.</summary>
    public required string DocumentTitle { get; set; }

    /// <summary>Nearest section heading above this chunk, used for precise citations.</summary>
    public required string Heading { get; set; }

    /// <summary>Zero-based position of this chunk within its source document.</summary>
    public int ChunkIndex { get; set; }

    public required string Content { get; set; }

    /// <summary>
    /// Embedding vector, stored as pgvector <c>vector(N)</c>. The CLR type is
    /// <c>float[]</c> here so the domain stays free of database packages; the
    /// infrastructure layer maps it onto the pgvector column type.
    /// </summary>
    public float[] Embedding { get; set; } = [];

    /// <summary>
    /// SHA-256 of <see cref="Content"/>. Lets ingestion skip re-embedding
    /// unchanged chunks, which is the expensive part of indexing.
    /// </summary>
    public required string ContentHash { get; set; }

    public DateTimeOffset IndexedAt { get; set; }
}
