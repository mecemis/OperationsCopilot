using OperationsCopilot.Domain.Chat;
using OperationsCopilot.Domain.Knowledge;
using OperationsCopilot.Domain.Planning;

namespace OperationsCopilot.Domain.Abstractions;

/// <summary>
/// Per-request scratchpad that collects what the agent did while answering, so the
/// response can report tools and citations without the tools themselves knowing
/// anything about the HTTP layer. Registered as a scoped service.
/// </summary>
public interface IToolCallRecorder
{
    IReadOnlyList<ToolInvocation> ToolCalls { get; }

    IReadOnlyList<KnowledgeSearchResult> RetrievedPassages { get; }

    /// <summary>
    /// The same calls as <see cref="ToolCalls"/>, but with arguments kept whole so the sequence
    /// can be replayed rather than only displayed.
    /// </summary>
    IReadOnlyList<PlannedToolCall> PlannedCalls { get; }

    void RecordToolCall(ToolInvocation invocation);

    /// <summary>Records a call in replayable form, for the semantic plan cache.</summary>
    void RecordPlannedCall(PlannedToolCall call);

    /// <summary>Records passages returned by knowledge-base search, de-duplicated by chunk id.</summary>
    void RecordRetrieval(IReadOnlyList<KnowledgeSearchResult> results);
}
