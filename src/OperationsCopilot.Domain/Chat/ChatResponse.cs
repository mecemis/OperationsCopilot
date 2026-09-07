namespace OperationsCopilot.Domain.Chat;

/// <summary>The agent's reply, with everything needed to audit how it was produced.</summary>
/// <param name="Answer">Natural-language answer for the user.</param>
/// <param name="ConversationId">Pass to the next request to continue this conversation.</param>
/// <param name="Citations">Knowledge-base passages the answer draws on, best match first.</param>
/// <param name="ToolCalls">Tools the agent chose to call, in invocation order.</param>
/// <param name="LatencyMs">Wall-clock time for the whole request, in milliseconds.</param>
public sealed record ChatResponse(
    string Answer,
    string ConversationId,
    IReadOnlyList<Citation> Citations,
    IReadOnlyList<ToolInvocation> ToolCalls,
    long LatencyMs)
{
    /// <summary>Token usage, when the model reported it.</summary>
    public TokenUsage? Usage { get; init; }

    /// <summary>What the semantic plan cache did this turn. Null when the cache is switched off.</summary>
    public PlanCacheTrace? PlanCache { get; init; }
}

/// <summary>
/// The semantic plan cache's contribution to one turn.
/// </summary>
/// <remarks>
/// Reported for the same reason the tool calls are: an answer produced from a reused plan was
/// reached differently from one the model planned itself, and hiding that would make the trace
/// less honest than the thing it describes. A reused plan still ran its tools against live data,
/// so the figures are current either way — what changed is who chose the tools.
/// </remarks>
/// <param name="Hit">True when a stored plan was found and replayed.</param>
public sealed record PlanCacheTrace(bool Hit)
{
    /// <summary>Similarity between this question and the one that produced the stored plan.</summary>
    public double? Similarity { get; init; }

    /// <summary>The question the reused plan was originally generated for.</summary>
    public string? MatchedQuestion { get; init; }

    public DateTimeOffset? PlanCapturedAt { get; init; }

    /// <summary>Turns that had already replayed this plan before this one.</summary>
    public int? TimesReused { get; init; }

    /// <summary>True when this turn's own plan was written to the cache for later reuse.</summary>
    public bool Stored { get; init; }

    /// <summary>Why the cache was not consulted, or not written to, when it wasn't.</summary>
    public string? Note { get; init; }
}

/// <summary>A knowledge-base passage the agent retrieved while answering.</summary>
/// <param name="Reference">Stable citation marker used in the answer text, e.g. <c>[1]</c>.</param>
/// <param name="Excerpt">Trimmed passage text, for showing the user why the answer says what it says.</param>
/// <param name="Score">Cosine similarity of this passage to the query, in [0, 1].</param>
public sealed record Citation(
    string Reference,
    string SourceFile,
    string DocumentTitle,
    string Heading,
    string Excerpt,
    double Score);

/// <summary>One tool the agent invoked, with its own timing.</summary>
/// <param name="Arguments">Arguments the model supplied, as name/value pairs rendered for display.</param>
/// <param name="Succeeded">False when the tool threw; <paramref name="Error"/> then carries the reason.</param>
public sealed record ToolInvocation(
    string PluginName,
    string FunctionName,
    IReadOnlyDictionary<string, string?> Arguments,
    long DurationMs,
    bool Succeeded,
    string? Error = null)
{
    /// <summary>Fully qualified tool name, e.g. <c>Operations.GetLowStockProducts</c>.</summary>
    public string Name => $"{PluginName}.{FunctionName}";
}

/// <summary>Model token usage for one request.</summary>
public sealed record TokenUsage(int PromptTokens, int CompletionTokens)
{
    public int TotalTokens => PromptTokens + CompletionTokens;
}
