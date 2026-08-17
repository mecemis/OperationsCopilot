using System.Net.Http.Json;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.AzureOpenAI;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using Microsoft.Extensions.Options;
using OperationsCopilot.Infrastructure.Ai;
using OperationsCopilot.Infrastructure.Options;

namespace OperationsCopilot.TestSupport;

/// <summary>
/// Resolves a real chat model for the live evaluation tier, from whichever provider the machine
/// actually has.
/// </summary>
/// <remarks>
/// Azure OpenAI wins when its endpoint is configured; otherwise a local Ollama server is used if
/// one is reachable. That means the tool-selection evaluations run for free on a developer
/// machine with Ollama, instead of only for people holding cloud credentials — and the
/// evaluations are worth far more when they actually get run.
/// </remarks>
public sealed record LiveModelSettings(AiProvider Provider, string ModelId, AiClientFactory Factory)
{
    public const string SkipReason =
        "No live chat model available. Either set AZURE_OPENAI_ENDPOINT (plus AZURE_OPENAI_API_KEY, " +
        "or sign in for DefaultAzureCredential), or run Ollama locally with a tool-calling model " +
        "such as qwen2.5:14b.";

    /// <summary>Human-readable description for test output.</summary>
    public string Description => $"{Provider} / {ModelId}";

    /// <summary>Builds the chat service the same way the application does for this provider.</summary>
    public IChatCompletionService CreateChatService() => Provider switch
    {
        AiProvider.AzureOpenAI => new AzureOpenAIChatCompletionService(ModelId, Factory.CreateAzureOpenAIClient()),
        AiProvider.Ollama => new OpenAIChatCompletionService(ModelId, Factory.CreateOllamaClient()),
        _ => throw new InvalidOperationException($"Provider '{Provider}' cannot drive the agent."),
    };

    /// <summary>Detects an available provider, or null when neither is usable.</summary>
    public static async Task<LiveModelSettings?> ResolveAsync(CancellationToken cancellationToken = default)
    {
        var azureEndpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT");

        if (!string.IsNullOrWhiteSpace(azureEndpoint))
        {
            var azure = new AzureOpenAIOptions
            {
                Endpoint = azureEndpoint,
                ApiKey = Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY"),
                ChatDeployment = Environment.GetEnvironmentVariable("AZURE_OPENAI_CHAT_DEPLOYMENT") ?? "gpt-4o-mini",
                EmbeddingDeployment = Environment.GetEnvironmentVariable("AZURE_OPENAI_EMBEDDING_DEPLOYMENT") ?? "text-embedding-3-small",
            };

            return new LiveModelSettings(
                AiProvider.AzureOpenAI,
                azure.ChatDeployment,
                BuildFactory(AiProvider.AzureOpenAI, azure, new OllamaOptions()));
        }

        var ollama = new OllamaOptions
        {
            Endpoint = Environment.GetEnvironmentVariable("OLLAMA_ENDPOINT") ?? "http://localhost:11434/v1",
            ChatModel = Environment.GetEnvironmentVariable("OLLAMA_CHAT_MODEL") ?? "qwen2.5:14b",
            EmbeddingModel = Environment.GetEnvironmentVariable("OLLAMA_EMBEDDING_MODEL") ?? "nomic-embed-text",
        };

        return await HasModelAsync(ollama, cancellationToken)
            ? new LiveModelSettings(
                AiProvider.Ollama,
                ollama.ChatModel,
                BuildFactory(AiProvider.Ollama, new AzureOpenAIOptions(), ollama))
            : null;
    }

    /// <summary>
    /// Checks that Ollama is up <em>and</em> has the model pulled. A running server with the model
    /// missing would otherwise fail every evaluation with an error that looks like bad tool
    /// selection rather than a missing download.
    /// </summary>
    private static async Task<bool> HasModelAsync(OllamaOptions options, CancellationToken cancellationToken)
    {
        try
        {
            // The model list lives on the native API, one level up from the OpenAI-compatible /v1.
            var root = new Uri(options.Endpoint).GetLeftPart(UriPartial.Authority);

            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            var tags = await client.GetFromJsonAsync<OllamaTags>($"{root}/api/tags", cancellationToken);

            return tags?.Models?.Any(model =>
                string.Equals(model.Name, options.ChatModel, StringComparison.OrdinalIgnoreCase)) == true;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or UriFormatException)
        {
            return false;
        }
    }

    private static AiClientFactory BuildFactory(
        AiProvider provider,
        AzureOpenAIOptions azure,
        OllamaOptions ollama) =>
        new(
            Microsoft.Extensions.Options.Options.Create(new AiOptions
            {
                ChatProvider = provider,
                EmbeddingProvider = provider,
            }),
            Microsoft.Extensions.Options.Options.Create(azure),
            Microsoft.Extensions.Options.Options.Create(ollama));

    private sealed record OllamaTags(IReadOnlyList<OllamaModel>? Models);

    private sealed record OllamaModel(string Name);
}
