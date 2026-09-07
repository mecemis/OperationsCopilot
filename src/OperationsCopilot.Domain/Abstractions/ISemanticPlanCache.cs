using OperationsCopilot.Domain.Planning;

namespace OperationsCopilot.Domain.Abstractions;

/// <summary>
/// Remembers which tools the model chose for a question, and finds that plan again for a
/// question that means the same thing.
/// </summary>
/// <remarks>
/// Lookup is by meaning rather than by text because the same request arrives worded a dozen ways
/// — "what's running low?", "which items need reordering?", "anything below threshold?" — and an
/// exact-match cache would miss all but one of them. The key is an embedding of the question,
/// which is the only thing available before the model has been asked anything.
/// </remarks>
public interface ISemanticPlanCache
{
    /// <param name="today">
    /// The date the model would be told. Plans whose arguments name an explicit date are only
    /// returned when it matches, because such a plan encodes the day it was made on.
    /// </param>
    /// <returns>The closest plan above the configured similarity floor, or null.</returns>
    Task<PlanCacheHit?> FindAsync(
        string question,
        DateOnly today,
        CancellationToken cancellationToken = default);

    /// <summary>Stores the plan a model generated, replacing any near-duplicate already held.</summary>
    Task StoreAsync(
        string question,
        ToolPlan plan,
        DateOnly today,
        CancellationToken cancellationToken = default);

    /// <summary>Counts a successful replay, so entries can be evicted by how useful they are.</summary>
    Task RecordReuseAsync(Guid entryId, CancellationToken cancellationToken = default);

    /// <summary>Drops an entry that can no longer be replayed, such as one naming a renamed tool.</summary>
    Task RemoveAsync(Guid entryId, CancellationToken cancellationToken = default);
}
