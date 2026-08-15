using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OperationsCopilot.Domain.Abstractions;
using OperationsCopilot.Infrastructure.Ai;
using OperationsCopilot.Infrastructure.Conversations;
using OperationsCopilot.Infrastructure.Embeddings;
using OperationsCopilot.Infrastructure.Knowledge;
using OperationsCopilot.Infrastructure.Options;
using OperationsCopilot.Infrastructure.Persistence;
using OperationsCopilot.Infrastructure.Seeding;

namespace OperationsCopilot.Infrastructure;

/// <summary>Registers everything the infrastructure layer provides.</summary>
public static class InfrastructureServiceCollectionExtensions
{
    public const string ConnectionStringName = "OperationsDb";

    public static IServiceCollection AddOperationsCopilotInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<AiOptions>()
            .Bind(configuration.GetSection(AiOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(
                options => options.ChatProvider != AiProvider.Deterministic,
                "Ai:ChatProvider cannot be 'Deterministic'. The agent needs a real chat model to " +
                "decide which tools to call. Use 'AzureOpenAI' or 'Ollama'.")
            .ValidateOnStart();

        services.AddOptions<AzureOpenAIOptions>()
            .Bind(configuration.GetSection(AzureOpenAIOptions.SectionName));

        services.AddOptions<OllamaOptions>()
            .Bind(configuration.GetSection(OllamaOptions.SectionName));

        services.AddOptions<RagOptions>()
            .Bind(configuration.GetSection(RagOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        var connectionString = configuration.GetConnectionString(ConnectionStringName)
            ?? throw new InvalidOperationException(
                $"Connection string '{ConnectionStringName}' is not configured. See README.md for setup.");

        services.AddDbContext<OperationsDbContext>(options => options
            .UseNpgsql(connectionString, npgsql =>
            {
                npgsql.UseVector();
                npgsql.MigrationsAssembly(typeof(OperationsDbContext).Assembly.FullName);
                // Postgres restarts and transient network faults should not surface as 500s.
                npgsql.EnableRetryOnFailure(maxRetryCount: 3, TimeSpan.FromSeconds(5), errorCodesToAdd: null);
            })
            .UseSnakeCaseNamingConvention());

        services.TryAddTimeProvider();

        services.AddSingleton<AiClientFactory>();

        services.AddScoped<IOperationsRepository, OperationsRepository>();
        services.AddScoped<IKnowledgeBaseSearch, PgVectorKnowledgeBaseSearch>();
        services.AddSingleton<IKnowledgeDocumentSource, EmbeddedKnowledgeDocumentSource>();
        services.AddSingleton<IConversationStore, InMemoryConversationStore>();
        services.AddScoped<KnowledgeBaseIndexer>();
        services.AddScoped<SampleDataSeeder>();
        services.AddScoped<VectorSchema>();
        services.AddScoped<DatabaseInitializer>();

        services.AddEmbeddingService(configuration);

        return services;
    }

    /// <summary>
    /// Registers the embedding implementation for the configured provider. The provider is read
    /// from configuration directly rather than resolved, because the choice decides which other
    /// services need registering at all.
    /// </summary>
    private static void AddEmbeddingService(this IServiceCollection services, IConfiguration configuration)
    {
        var provider = configuration
            .GetSection(AiOptions.SectionName)
            .GetValue(nameof(AiOptions.EmbeddingProvider), AiProvider.Ollama);

        if (provider == AiProvider.Deterministic)
        {
            services.AddSingleton<IEmbeddingService, DeterministicEmbeddingService>();
            return;
        }

        if (provider == AiProvider.AzureOpenAI)
        {
            services.RequireAzureOpenAIConfiguration();
        }

        services.AddSingleton<IEmbeddingService>(sp =>
        {
            var factory = sp.GetRequiredService<AiClientFactory>();

            return new GeneratedEmbeddingService(
                factory.CreateEmbeddingGenerator(),
                factory.EmbeddingDimensions);
        });
    }

    /// <summary>
    /// Fails startup with an actionable message when a component that needs Azure OpenAI is
    /// registered without it being configured, instead of throwing on the first request.
    /// Registering it more than once is harmless: the validations simply both run.
    /// </summary>
    public static IServiceCollection RequireAzureOpenAIConfiguration(this IServiceCollection services)
    {
        services.AddOptions<AzureOpenAIOptions>()
            .Validate(
                options => options.IsConfigured,
                "AzureOpenAI:Endpoint must be set to an absolute URL, for example " +
                "https://my-resource.openai.azure.com/, because the Ai section selects the " +
                "AzureOpenAI provider. To run entirely locally, set Ai:ChatProvider and " +
                "Ai:EmbeddingProvider to 'Ollama'.")
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.ChatDeployment),
                "AzureOpenAI:ChatDeployment must name a deployment in the configured resource.")
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.EmbeddingDeployment),
                "AzureOpenAI:EmbeddingDeployment must name a deployment in the configured resource.")
            .ValidateOnStart();

        return services;
    }

    private static void TryAddTimeProvider(this IServiceCollection services)
    {
        if (services.All(d => d.ServiceType != typeof(TimeProvider)))
        {
            services.AddSingleton(TimeProvider.System);
        }
    }
}
