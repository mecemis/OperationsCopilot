using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OperationsCopilot.Domain.Abstractions;
using OperationsCopilot.Infrastructure.Knowledge;
using OperationsCopilot.Infrastructure.Options;
using OperationsCopilot.Infrastructure.Seeding;

namespace OperationsCopilot.Infrastructure.Persistence;

/// <summary>
/// Brings a database up to a usable state on startup: apply migrations, seed the demo data,
/// then index the knowledge base.
/// </summary>
/// <remarks>
/// Running migrations from application startup suits a self-contained sample that has to work
/// from a single <c>docker compose up</c>. A real deployment should apply migrations from a
/// release pipeline instead, so that schema changes are gated and do not race across instances.
/// </remarks>
public sealed class DatabaseInitializer(
    OperationsDbContext dbContext,
    SampleDataSeeder seeder,
    KnowledgeBaseIndexer indexer,
    VectorSchema vectorSchema,
    IEmbeddingService embeddingService,
    IOptions<RagOptions> ragOptions,
    ILogger<DatabaseInitializer> logger)
{
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Applying database migrations.");
        await dbContext.Database.MigrateAsync(cancellationToken);

        // Must run before indexing: the column has to match the configured embedding model, and
        // switching models clears the knowledge base so it can be rebuilt below.
        await vectorSchema.EnsureDimensionsAsync(embeddingService.Dimensions, cancellationToken);

        await seeder.SeedAsync(cancellationToken);

        if (ragOptions.Value.IndexOnStartup)
        {
            await indexer.IndexAsync(cancellationToken);
        }
        else
        {
            logger.LogInformation("Knowledge base indexing on startup is disabled.");
        }
    }
}
