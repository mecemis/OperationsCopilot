using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.AzureOpenAI;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using OperationsCopilot.Agent.Filters;
using OperationsCopilot.Agent.Options;
using OperationsCopilot.Agent.Plugins;
using OperationsCopilot.Domain.Abstractions;
using OperationsCopilot.Infrastructure;
using OperationsCopilot.Infrastructure.Ai;
using OperationsCopilot.Infrastructure.Options;

namespace OperationsCopilot.Agent;

/// <summary>Registers the Semantic Kernel agent, its tools, and its filters.</summary>
public static class AgentServiceCollectionExtensions
{
    /// <summary>Plugin name the model sees for the database tools.</summary>
    public const string OperationsPluginName = "Operations";

    /// <summary>Plugin name the model sees for the retrieval tool.</summary>
    public const string KnowledgePluginName = "KnowledgeBase";

    /// <summary>
    /// Registers the agent and the chat model that drives it, for whichever provider the
    /// <c>Ai</c> section selects. This is what the application uses.
    /// </summary>
    public static IServiceCollection AddOperationsCopilotAgent(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var provider = configuration
            .GetSection(AiOptions.SectionName)
            .GetValue(nameof(AiOptions.ChatProvider), AiProvider.Ollama);

        if (provider == AiProvider.AzureOpenAI)
        {
            services.RequireAzureOpenAIConfiguration();
        }

        services.AddSingleton<IChatCompletionService>(sp =>
        {
            var factory = sp.GetRequiredService<AiClientFactory>();

            // Ollama goes through the stock OpenAI connector against its OpenAI-compatible API,
            // so automatic function calling takes the identical code path on both providers.
            // A dedicated Ollama connector would be a second, differently-behaved path.
            return factory.ChatProvider switch
            {
                AiProvider.AzureOpenAI => new AzureOpenAIChatCompletionService(
                    factory.ChatModelId,
                    factory.CreateAzureOpenAIClient()),

                AiProvider.Ollama => new OpenAIChatCompletionService(
                    factory.ChatModelId,
                    factory.CreateOllamaClient()),

                _ => throw new InvalidOperationException(
                    $"Ai:ChatProvider '{factory.ChatProvider}' cannot drive the agent."),
            };
        });

        return services.AddOperationsCopilotAgentCore(configuration);
    }

    /// <summary>
    /// Registers everything about the agent except the chat model: options, the tool plugins,
    /// the recorder, the tracking filter, the kernel, and the agent itself.
    /// </summary>
    /// <remarks>
    /// Split out from <see cref="AddOperationsCopilotAgent"/> so that tests and evaluations can
    /// supply their own <see cref="IChatCompletionService"/> and exercise the real tool wiring
    /// without an Azure subscription. Keeping it as one registration path means the wiring under
    /// test is the wiring that ships.
    /// </remarks>
    public static IServiceCollection AddOperationsCopilotAgentCore(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<CopilotAgentOptions>()
            .Bind(configuration.GetSection(CopilotAgentOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // Scoped throughout: the recorder and the tools that write to it must not be shared
        // between concurrent requests, and the kernel holds the filter that writes to it.
        services.AddScoped<IToolCallRecorder, ToolCallRecorder>();
        services.AddScoped<ToolCallTrackingFilter>();
        services.AddScoped<OperationsPlugin>();
        services.AddScoped<KnowledgeBasePlugin>();
        services.AddScoped(BuildKernel);
        services.AddScoped<ICopilotAgent, CopilotAgent>();

        return services;
    }

    /// <summary>
    /// Builds a kernel bound to the current request scope. Constructing it from the scope's
    /// <see cref="IServiceProvider"/> is what lets a tool resolve scoped services such as the
    /// DbContext, and lets the filter write to this request's recorder.
    /// </summary>
    private static Kernel BuildKernel(IServiceProvider serviceProvider)
    {
        var kernel = new Kernel(serviceProvider);

        kernel.Plugins.AddFromObject(
            serviceProvider.GetRequiredService<OperationsPlugin>(),
            OperationsPluginName);

        kernel.Plugins.AddFromObject(
            serviceProvider.GetRequiredService<KnowledgeBasePlugin>(),
            KnowledgePluginName);

        kernel.FunctionInvocationFilters.Add(serviceProvider.GetRequiredService<ToolCallTrackingFilter>());

        return kernel;
    }
}
