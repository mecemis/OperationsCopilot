using System.ComponentModel.DataAnnotations;

namespace OperationsCopilot.Infrastructure.Options;

/// <summary>Which service backs a model call.</summary>
public enum AiProvider
{
    /// <summary>An Azure OpenAI resource. Deployment names, Entra ID or an API key.</summary>
    AzureOpenAI,

    /// <summary>A local Ollama server, reached through its OpenAI-compatible API.</summary>
    Ollama,

    /// <summary>
    /// Hashed bag-of-words vectors computed in process. Embeddings only — there is no local
    /// substitute for the chat model, because choosing tools is the model's whole job.
    /// </summary>
    Deterministic,
}

/// <summary>
/// Selects the provider for each kind of model call, bound from the <c>Ai</c> section.
/// </summary>
/// <remarks>
/// Chat and embeddings are chosen independently on purpose. They are separate concerns with
/// separate trade-offs: you might want a local chat model for cost while keeping Azure
/// embeddings for retrieval quality, or the reverse. Each provider's own connection settings
/// live in <see cref="AzureOpenAIOptions"/> and <see cref="OllamaOptions"/>.
/// </remarks>
public sealed class AiOptions
{
    public const string SectionName = "Ai";

    /// <summary>Provider for the agent's chat model. <see cref="AiProvider.Deterministic"/> is not valid here.</summary>
    public AiProvider ChatProvider { get; set; } = AiProvider.Ollama;

    /// <summary>Provider used to embed documents at index time and queries at search time.</summary>
    public AiProvider EmbeddingProvider { get; set; } = AiProvider.Ollama;
}

/// <summary>Connection settings for a local Ollama server, bound from the <c>Ollama</c> section.</summary>
/// <remarks>
/// Ollama is reached over its OpenAI-compatible API rather than its native one, so it goes
/// through exactly the same Semantic Kernel connector as Azure OpenAI. That means automatic
/// function calling behaves identically on both, and there is one code path to reason about
/// instead of two.
/// </remarks>
public sealed class OllamaOptions
{
    public const string SectionName = "Ollama";

    /// <summary>
    /// Base URL of the OpenAI-compatible API. Note the <c>/v1</c> suffix: Ollama serves its
    /// native API at the root and the OpenAI-compatible one under <c>/v1</c>.
    /// </summary>
    [Required]
    [Url]
    public string Endpoint { get; set; } = "http://localhost:11434/v1";

    /// <summary>
    /// Chat model tag. It <em>must</em> support tool calling — this agent cannot work without it.
    /// </summary>
    /// <remarks>
    /// Size matters here beyond the usual quality argument. Measured against this project's own
    /// tool-selection evaluations, qwen2.5:7b picks the right single tool every time but never
    /// chains two in one turn, so every question needing both live data and a written rule is
    /// answered from half the evidence. qwen2.5:14b chains reliably. The default is therefore the
    /// larger model; drop to 7b only if single-tool questions are all you need.
    /// </remarks>
    [Required]
    public string ChatModel { get; set; } = "qwen2.5:14b";

    /// <summary>Embedding model tag, e.g. <c>nomic-embed-text</c>.</summary>
    [Required]
    public string EmbeddingModel { get; set; } = "nomic-embed-text";

    /// <summary>
    /// Output width of <see cref="EmbeddingModel"/>. Must match the model exactly:
    /// nomic-embed-text is 768, mxbai-embed-large and bge-m3 are 1024, all-minilm is 384.
    /// </summary>
    [Range(64, 4096)]
    public int EmbeddingDimensions { get; set; } = 768;

    /// <summary>
    /// Ollama ignores the credential, but the OpenAI client requires one to be present. This is
    /// a placeholder, not a secret.
    /// </summary>
    public string ApiKey { get; set; } = "ollama";
}
