namespace OperationsCopilot.Domain.Planning;

/// <summary>
/// A stored plan: the question that produced it, the vector it is found by, and the tool calls
/// to replay.
/// </summary>
/// <remarks>
/// Sits beside <see cref="Knowledge.DocumentChunk"/> as the second vector-indexed table, and
/// follows the same rule — the embedding is a plain <c>float[]</c> here, and the infrastructure
/// layer maps it onto pgvector's column type.
/// </remarks>
public class CachedToolPlan
{
    public Guid Id { get; set; }

    /// <summary>The question as the user asked it, kept so a cache hit can be shown its origin.</summary>
    public required string Question { get; set; }

    /// <summary>Embedding of <see cref="Question"/>, and the only thing lookup matches on.</summary>
    public float[] Embedding { get; set; } = [];

    /// <summary>The generated tool calls, in the order the model made them.</summary>
    public required IReadOnlyList<PlannedToolCall> Calls { get; set; }

    /// <summary>Mirrors <see cref="ToolPlan.IsDateAnchored"/>, stored so lookup can filter in SQL.</summary>
    public bool IsDateAnchored { get; set; }

    /// <summary>The date the model was told when it generated this plan.</summary>
    public DateOnly CapturedOn { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>When this plan was last replayed, or when it was stored if it never has been.</summary>
    public DateTimeOffset LastUsedAt { get; set; }

    /// <summary>Turns that have replayed this plan. Drives eviction: unused plans go first.</summary>
    public int TimesReused { get; set; }

    public ToolPlan ToPlan() => new(Calls);
}
