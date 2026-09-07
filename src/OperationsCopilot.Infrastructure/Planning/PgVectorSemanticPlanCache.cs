using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OperationsCopilot.Domain.Abstractions;
using OperationsCopilot.Domain.Planning;
using OperationsCopilot.Infrastructure.Options;
using OperationsCopilot.Infrastructure.Persistence;
using Pgvector;

namespace OperationsCopilot.Infrastructure.Planning;

/// <summary>
/// The semantic plan cache, over the same pgvector instance that holds the knowledge base.
/// </summary>
/// <remarks>
/// <para>
/// Reuses pgvector rather than adding Redis or a dedicated cache service. The database is already
/// here, already has the extension, and already holds vectors written by the same embedding
/// model — a second store would add an operational dependency to buy nothing this cache needs.
/// </para>
/// <para>
/// Lookup is raw SQL for the same reason as
/// <see cref="Knowledge.PgVectorKnowledgeBaseSearch"/>: the <c>&lt;=&gt;</c> operator in the
/// ORDER BY is what makes PostgreSQL choose the HNSW index, and writing it out keeps that
/// visible. Writes go through EF so the embedding and plan value converters apply.
/// </para>
/// </remarks>
public sealed class PgVectorSemanticPlanCache(
    OperationsDbContext dbContext,
    IEmbeddingService embeddingService,
    IOptions<SemanticCacheOptions> cacheOptions,
    TimeProvider timeProvider,
    ILogger<PgVectorSemanticPlanCache> logger) : ISemanticPlanCache
{
    private readonly SemanticCacheOptions _options = cacheOptions.Value;

    public async Task<PlanCacheHit?> FindAsync(
        string question,
        DateOnly today,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(question))
        {
            return null;
        }

        var embedding = new Vector(await embeddingService.EmbedAsync(question, cancellationToken));
        var candidates = await QueryNearestAsync(embedding, today, _options.Candidates, cancellationToken);

        foreach (var candidate in candidates)
        {
            var similarity = 1d - candidate.Distance;

            // Rows arrive nearest first, so the first one under the floor ends the search.
            if (similarity < _options.MinimumSimilarity)
            {
                break;
            }

            // Close in meaning is not the same as about the same thing. "Tell me about PT-1001"
            // and "Tell me about PT-1006" sit at 0.87 — well inside the paraphrase band — and
            // replaying one for the other would report a real product's real figures under the
            // wrong question.
            if (!QuestionDiscriminators.Match(question, candidate.Question))
            {
                logger.LogDebug(
                    "Plan for {Matched} scored {Similarity:F3} but names different subjects; skipped.",
                    candidate.Question,
                    similarity);

                continue;
            }

            logger.LogInformation(
                "Reusing the plan generated for {Matched} at similarity {Similarity:F3}.",
                candidate.Question,
                similarity);

            return new PlanCacheHit(
                candidate.Id,
                candidate.Question,
                new ToolPlan(ToolPlanSerializer.Deserialize(candidate.Calls)),
                Math.Round(similarity, 4),
                candidate.CreatedAt,
                candidate.TimesReused);
        }

        return null;
    }

    public async Task StoreAsync(
        string question,
        ToolPlan plan,
        DateOnly today,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(question) || plan.Calls.Count == 0)
        {
            return;
        }

        // Embedded a second time on a miss, having already been embedded by FindAsync. Passing
        // the vector between the two calls would put Pgvector's types on ISemanticPlanCache, and
        // this runs after the model has answered, where one more embedding call is not the cost
        // that matters.
        var vector = await embeddingService.EmbedAsync(question, cancellationToken);
        var now = timeProvider.GetUtcNow();

        await ReplaceNearDuplicatesAsync(new Vector(vector), question, today, cancellationToken);

        dbContext.CachedToolPlans.Add(new CachedToolPlan
        {
            Id = Guid.CreateVersion7(),
            Question = question,
            Embedding = vector,
            Calls = plan.Calls,
            IsDateAnchored = plan.IsDateAnchored,
            CapturedOn = today,
            CreatedAt = now,
            LastUsedAt = now,
            TimesReused = 0,
        });

        await dbContext.SaveChangesAsync(cancellationToken);
        await EvictAsync(now, cancellationToken);

        logger.LogInformation(
            "Cached a {CallCount}-call plan for {Question} ({Anchoring}).",
            plan.Calls.Count,
            question,
            plan.IsDateAnchored ? $"valid for {today:yyyy-MM-dd} only" : "not date-anchored");
    }

    public async Task RecordReuseAsync(Guid entryId, CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow();

        await dbContext.CachedToolPlans
            .Where(plan => plan.Id == entryId)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(plan => plan.TimesReused, plan => plan.TimesReused + 1)
                    .SetProperty(plan => plan.LastUsedAt, now),
                cancellationToken);
    }

    public async Task RemoveAsync(Guid entryId, CancellationToken cancellationToken = default)
    {
        await dbContext.CachedToolPlans
            .Where(plan => plan.Id == entryId)
            .ExecuteDeleteAsync(cancellationToken);
    }

    /// <summary>
    /// Drops entries the new plan supersedes, so that a dozen wordings of one question do not
    /// each occupy a row and push more useful plans out of the cache.
    /// </summary>
    private async Task ReplaceNearDuplicatesAsync(
        Vector embedding,
        string question,
        DateOnly today,
        CancellationToken cancellationToken)
    {
        var candidates = await QueryNearestAsync(embedding, today, _options.Candidates, cancellationToken);

        var superseded = candidates
            .Where(candidate => 1d - candidate.Distance >= _options.MinimumSimilarity)
            .Where(candidate => QuestionDiscriminators.Match(question, candidate.Question))
            .Select(candidate => candidate.Id)
            .ToList();

        if (superseded.Count == 0)
        {
            return;
        }

        await dbContext.CachedToolPlans
            .Where(plan => superseded.Contains(plan.Id))
            .ExecuteDeleteAsync(cancellationToken);
    }

    /// <summary>
    /// Nearest stored questions, already filtered by the two rules that have nothing to do with
    /// distance: the entry must still be live, and a plan carrying an explicit date is only
    /// offered on the day it was made.
    /// </summary>
    private async Task<IReadOnlyList<PlanRow>> QueryNearestAsync(
        Vector embedding,
        DateOnly today,
        int limit,
        CancellationToken cancellationToken)
    {
        var cutoff = timeProvider.GetUtcNow() - _options.TimeToLive;

        return await dbContext.Database
            .SqlQuery<PlanRow>(
                $"""
                 SELECT   id,
                          question,
                          calls,
                          created_at,
                          times_reused,
                          (embedding <=> {embedding}) AS distance
                 FROM     semantic_plan_cache
                 WHERE    created_at >= {cutoff}
                   AND    (NOT is_date_anchored OR captured_on = {today})
                 ORDER BY embedding <=> {embedding}
                 LIMIT    {limit}
                 """)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Removes expired entries, then trims to <see cref="SemanticCacheOptions.MaxEntries"/>,
    /// dropping the plans that have earned their place least.
    /// </summary>
    private async Task EvictAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var cutoff = now - _options.TimeToLive;

        var expired = await dbContext.CachedToolPlans
            .Where(plan => plan.CreatedAt < cutoff)
            .ExecuteDeleteAsync(cancellationToken);

        var count = await dbContext.CachedToolPlans.CountAsync(cancellationToken);
        var excess = count - _options.MaxEntries;

        if (excess <= 0)
        {
            if (expired > 0)
            {
                logger.LogDebug("Evicted {Expired} expired plan(s).", expired);
            }

            return;
        }

        // Least reused first, and among those the least recently used: a plan nobody has come
        // back to is the cheapest one to lose.
        var doomed = dbContext.CachedToolPlans
            .OrderBy(plan => plan.TimesReused)
            .ThenBy(plan => plan.LastUsedAt)
            .Take(excess)
            .Select(plan => plan.Id);

        var trimmed = await dbContext.CachedToolPlans
            .Where(plan => doomed.Contains(plan.Id))
            .ExecuteDeleteAsync(cancellationToken);

        logger.LogInformation(
            "Evicted {Expired} expired and {Trimmed} least-used plan(s); cache capped at {MaxEntries}.",
            expired,
            trimmed,
            _options.MaxEntries);
    }

    /// <summary>
    /// Row shape for the raw vector query. As elsewhere, the SQL selects the underlying
    /// snake_case column names and EF maps them onto these properties.
    /// </summary>
    private sealed record PlanRow(
        Guid Id,
        string Question,
        string Calls,
        DateTimeOffset CreatedAt,
        int TimesReused,
        double Distance);
}
