using System.ClientModel;
using Azure;
using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using OpenAI;
using OperationsCopilot.Infrastructure.Options;

namespace OperationsCopilot.Infrastructure.Ai;

/// <summary>
/// Builds the model clients for whichever providers are configured.
/// </summary>
/// <remarks>
/// Provider choice, endpoints and credentials are infrastructure concerns, so they are resolved
/// here rather than in the agent layer. The agent asks this factory for a client and wires it
/// into Semantic Kernel, which keeps the "which provider" decision in exactly one place.
/// </remarks>
public sealed class AiClientFactory(
    IOptions<AiOptions> aiOptions,
    IOptions<AzureOpenAIOptions> azureOptions,
    IOptions<OllamaOptions> ollamaOptions)
{
    private readonly AiOptions _ai = aiOptions.Value;
    private readonly AzureOpenAIOptions _azure = azureOptions.Value;
    private readonly OllamaOptions _ollama = ollamaOptions.Value;

    public AiProvider ChatProvider => _ai.ChatProvider;

    public AiProvider EmbeddingProvider => _ai.EmbeddingProvider;

    /// <summary>Model or deployment name for the configured chat provider.</summary>
    public string ChatModelId => _ai.ChatProvider switch
    {
        AiProvider.AzureOpenAI => _azure.ChatDeployment,
        AiProvider.Ollama => _ollama.ChatModel,
        _ => throw UnsupportedChatProvider(_ai.ChatProvider),
    };

    /// <summary>
    /// Vector width the configured embedding provider produces, and therefore the width the
    /// pgvector column has to be.
    /// </summary>
    public int EmbeddingDimensions => _ai.EmbeddingProvider switch
    {
        AiProvider.AzureOpenAI => _azure.EmbeddingDimensions,
        AiProvider.Ollama => _ollama.EmbeddingDimensions,
        AiProvider.Deterministic => DeterministicEmbeddingDimensions,
        _ => throw new InvalidOperationException($"Unknown embedding provider '{_ai.EmbeddingProvider}'."),
    };

    /// <summary>
    /// Width of the offline provider's vectors. Arbitrary but fixed; wide enough that hashed
    /// terms rarely collide.
    /// </summary>
    public const int DeterministicEmbeddingDimensions = 1536;

    /// <summary>
    /// One client for Azure OpenAI. Prefers Entra ID; an API key is supported for local
    /// development, where issuing a key is often quicker than granting a role assignment.
    /// </summary>
    public AzureOpenAIClient CreateAzureOpenAIClient()
    {
        if (!_azure.IsConfigured)
        {
            throw new InvalidOperationException(
                "AzureOpenAI:Endpoint is not set to an absolute URL, but the Ai section selects " +
                "the AzureOpenAI provider. Set the endpoint, or switch Ai:ChatProvider and " +
                "Ai:EmbeddingProvider to 'Ollama'. See README.md.");
        }

        var endpoint = new Uri(_azure.Endpoint);

        return _azure.UsesApiKey
            ? new AzureOpenAIClient(endpoint, new AzureKeyCredential(_azure.ApiKey!))
            : new AzureOpenAIClient(endpoint, new DefaultAzureCredential());
    }

    /// <summary>
    /// A stock OpenAI client pointed at Ollama's OpenAI-compatible API.
    /// </summary>
    /// <remarks>
    /// Ollama ignores the credential entirely, but the client refuses to be constructed without
    /// one, hence the placeholder. Using the OpenAI client rather than a dedicated Ollama
    /// connector is what lets tool calling behave identically across both providers.
    /// </remarks>
    public OpenAIClient CreateOllamaClient() =>
        new(
            new ApiKeyCredential(_ollama.ApiKey),
            new OpenAIClientOptions { Endpoint = new Uri(_ollama.Endpoint) });

    /// <summary>Builds the embedding generator for the configured embedding provider.</summary>
    public IEmbeddingGenerator<string, Embedding<float>> CreateEmbeddingGenerator() =>
        _ai.EmbeddingProvider switch
        {
            AiProvider.AzureOpenAI => CreateAzureOpenAIClient()
                .GetEmbeddingClient(_azure.EmbeddingDeployment)
                .AsIEmbeddingGenerator(),

            AiProvider.Ollama => CreateOllamaClient()
                .GetEmbeddingClient(_ollama.EmbeddingModel)
                .AsIEmbeddingGenerator(),

            _ => throw new InvalidOperationException(
                $"Provider '{_ai.EmbeddingProvider}' does not use an embedding generator."),
        };

    private static InvalidOperationException UnsupportedChatProvider(AiProvider provider) =>
        new($"Ai:ChatProvider cannot be '{provider}'. The agent needs a real chat model to decide " +
            "which tools to call; there is no offline substitute. Use 'AzureOpenAI' or 'Ollama'.");
}
