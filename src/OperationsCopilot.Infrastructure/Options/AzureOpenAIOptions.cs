namespace OperationsCopilot.Infrastructure.Options;

/// <summary>Azure OpenAI connection settings, bound from the <c>AzureOpenAI</c> configuration section.</summary>
/// <remarks>
/// Deliberately free of <c>[Required]</c> annotations. These settings matter only when a
/// provider in the <c>Ai</c> section actually selects Azure OpenAI, and the app runs perfectly
/// well on Ollama with this section left empty. Validation is registered on demand, by
/// <c>InfrastructureServiceCollectionExtensions.RequireAzureOpenAIConfiguration</c>.
/// </remarks>
public sealed class AzureOpenAIOptions
{
    public const string SectionName = "AzureOpenAI";

    /// <summary>Resource endpoint, e.g. <c>https://my-resource.openai.azure.com/</c>.</summary>
    public string Endpoint { get; set; } = string.Empty;

    /// <summary>
    /// API key. Leave empty to authenticate with <c>DefaultAzureCredential</c>, which is the
    /// preferred option outside local development.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>Deployment name of the chat model that drives the agent, e.g. <c>gpt-4o-mini</c>.</summary>
    public string ChatDeployment { get; set; } = "gpt-4o-mini";

    /// <summary>Deployment name of the embedding model, e.g. <c>text-embedding-3-small</c>.</summary>
    public string EmbeddingDeployment { get; set; } = "text-embedding-3-small";

    /// <summary>
    /// Output width of <see cref="EmbeddingDeployment"/>. text-embedding-3-small and
    /// text-embedding-ada-002 are 1536; text-embedding-3-large is 3072.
    /// </summary>
    public int EmbeddingDimensions { get; set; } = 1536;

    /// <summary>True when an endpoint is configured and the Azure connectors can be used.</summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Endpoint) && Uri.TryCreate(Endpoint, UriKind.Absolute, out _);

    public bool UsesApiKey => !string.IsNullOrWhiteSpace(ApiKey);
}
