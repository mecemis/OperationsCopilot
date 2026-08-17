using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OperationsCopilot.Agent;
using OperationsCopilot.Infrastructure;
using OperationsCopilot.Infrastructure.Knowledge;
using OperationsCopilot.Infrastructure.Options;
using OperationsCopilot.Infrastructure.Persistence;
using OperationsCopilot.Infrastructure.Seeding;
using Xunit;

namespace OperationsCopilot.TestSupport;

/// <summary>
/// A migrated, seeded and fully indexed database, plus a service provider wired the way the
/// application wires it.
/// </summary>
/// <remarks>
/// Embeddings use the deterministic provider so the whole suite runs offline, for free, and
/// gives the same answer on every machine. That is what lets the retrieval evaluations assert
/// hard thresholds instead of eyeballing results.
/// </remarks>
public sealed class SeededDatabaseFixture : IAsyncLifetime
{
    private readonly PostgresFixture _postgres = new();
    private readonly List<ServiceProvider> _derivedProviders = [];
    private ServiceProvider? _services;

    public IServiceProvider Services => _services
        ?? throw new InvalidOperationException($"{nameof(InitializeAsync)} has not run yet.");

    public string ConnectionString => _postgres.ConnectionString;

    /// <summary>Result of the indexing pass, so tests can assert the knowledge base was populated.</summary>
    public IndexingResult IndexingResult { get; private set; } = new(0, 0, 0);

    public AsyncServiceScope CreateScope() => Services.CreateAsyncScope();

    /// <summary>
    /// Builds an additional provider against the same database, letting a test add or replace
    /// registrations — most usefully a scripted chat model in place of Azure OpenAI.
    /// </summary>
    /// <remarks>Disposed with the fixture, so callers do not have to track it.</remarks>
    public IServiceProvider BuildServices(Action<IServiceCollection> configure)
    {
        var provider = BuildProvider(configure);
        _derivedProviders.Add(provider);
        return provider;
    }

    public async ValueTask InitializeAsync()
    {
        await _postgres.InitializeAsync();

        _services = BuildProvider(configure: null);

        await using var scope = _services.CreateAsyncScope();

        await scope.ServiceProvider.GetRequiredService<OperationsDbContext>()
            .Database.MigrateAsync();

        await scope.ServiceProvider.GetRequiredService<SampleDataSeeder>()
            .SeedAsync();

        IndexingResult = await scope.ServiceProvider.GetRequiredService<KnowledgeBaseIndexer>()
            .IndexAsync();
    }

    private ServiceProvider BuildProvider(Action<IServiceCollection>? configure)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"ConnectionStrings:{InfrastructureServiceCollectionExtensions.ConnectionStringName}"] =
                    _postgres.ConnectionString,
                [$"{AiOptions.SectionName}:{nameof(AiOptions.EmbeddingProvider)}"] =
                    nameof(AiProvider.Deterministic),
                // Never reached: the fixture supplies its own chat service. Set so that options
                // validation, which forbids a Deterministic chat provider, still passes.
                [$"{AiOptions.SectionName}:{nameof(AiOptions.ChatProvider)}"] =
                    nameof(AiProvider.Ollama),
                // Indexing is driven explicitly in InitializeAsync so its result can be asserted.
                [$"{RagOptions.SectionName}:{nameof(RagOptions.IndexOnStartup)}"] = "false",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Warning));
        services.AddOperationsCopilotInfrastructure(configuration);

        // The agent minus its chat model: real plugins, real filter, real recorder. Tests supply
        // whatever stands in for the model.
        services.AddOperationsCopilotAgentCore(configuration);

        configure?.Invoke(services);

        return services.BuildServiceProvider();
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var provider in _derivedProviders)
        {
            await provider.DisposeAsync();
        }

        if (_services is not null)
        {
            await _services.DisposeAsync();
        }

        await _postgres.DisposeAsync();
    }
}
