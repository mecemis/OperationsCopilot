using System.Text.Json.Serialization;

namespace OperationsCopilot.Domain.Planning;

/// <summary>
/// One tool call the model generated, captured faithfully enough to run again.
/// </summary>
/// <remarks>
/// Argument values are kept as the strings the model supplied rather than as typed values.
/// Semantic Kernel converts a string into the parameter's own type when it invokes the function,
/// which is the same conversion the live path performs, so a replayed call reaches the tool
/// exactly as the original did. Unlike <see cref="Chat.ToolInvocation"/> these values are never
/// truncated: that record exists to be displayed, this one exists to be executed, and a
/// shortened argument would silently run a different query.
/// </remarks>
public sealed record PlannedToolCall(
    string PluginName,
    string FunctionName,
    IReadOnlyDictionary<string, string?> Arguments)
{
    /// <summary>Fully qualified tool name, e.g. <c>Operations.GetLowStockProducts</c>.</summary>
    /// <remarks>Derived, so it is not persisted with the plan.</remarks>
    [JsonIgnore]
    public string Name => $"{PluginName}.{FunctionName}";
}

/// <summary>
/// The sequence of tool calls a model generated for one question — the part of a turn worth
/// caching, because it is what the model was asked to decide.
/// </summary>
/// <remarks>
/// A plan is not an answer. Replaying it runs the tools again against live data and asks the
/// model to write the answer from what comes back, so a cached plan can never serve a stale
/// stock level or sales figure. What it saves is the model round trip that chose the tools.
/// </remarks>
public sealed record ToolPlan(IReadOnlyList<PlannedToolCall> Calls)
{
    /// <summary>
    /// True when any argument is a calendar date, which means the plan only answers the question
    /// it was generated for on the day it was generated.
    /// </summary>
    /// <remarks>
    /// The system prompt tells the model today's date and the model resolves "last month" into
    /// explicit <c>yyyy-MM-dd</c> bounds. Those bounds are correct on the day they were chosen
    /// and wrong afterwards, so a plan carrying one is only reusable while the model's "today" is
    /// unchanged. Plans with no date argument — every knowledge-base search, and relative windows
    /// such as <c>lastDays=30</c> — carry no such anchor and stay reusable.
    /// </remarks>
    public bool IsDateAnchored => Calls
        .SelectMany(call => call.Arguments.Values)
        .Any(LooksLikeDate);

    /// <summary>
    /// Recognises the <c>yyyy-MM-dd</c> form the tools document and the model therefore emits.
    /// Deliberately narrow: a false positive only costs a cache entry its cross-day lifetime,
    /// while a false negative would replay a stale date range.
    /// </summary>
    private static bool LooksLikeDate(string? value) =>
        value is { Length: 10 }
        && DateOnly.TryParseExact(value, "yyyy-MM-dd", out _);
}

/// <summary>A plan found in the cache for a question close enough to the one being asked.</summary>
/// <param name="Similarity">Cosine similarity between the two questions, in [0, 1].</param>
/// <param name="MatchedQuestion">The question that originally produced this plan.</param>
/// <param name="TimesReused">How many turns have replayed it before this one.</param>
public sealed record PlanCacheHit(
    Guid EntryId,
    string MatchedQuestion,
    ToolPlan Plan,
    double Similarity,
    DateTimeOffset CapturedAt,
    int TimesReused);
