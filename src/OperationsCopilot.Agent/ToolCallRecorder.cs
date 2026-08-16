using OperationsCopilot.Domain.Abstractions;
using OperationsCopilot.Domain.Chat;
using OperationsCopilot.Domain.Knowledge;

namespace OperationsCopilot.Agent;

/// <summary>
/// Scoped, per-request record of what the agent did. Registered per scope so that concurrent
/// requests cannot see each other's tool calls or citations.
/// </summary>
public sealed class ToolCallRecorder : IToolCallRecorder
{
    private readonly List<ToolInvocation> _toolCalls = [];
    private readonly List<KnowledgeSearchResult> _retrieved = [];
    private readonly HashSet<Guid> _seenChunks = [];
    private readonly Lock _gate = new();

    public IReadOnlyList<ToolInvocation> ToolCalls
    {
        get
        {
            lock (_gate)
            {
                return [.. _toolCalls];
            }
        }
    }

    public IReadOnlyList<KnowledgeSearchResult> RetrievedPassages
    {
        get
        {
            lock (_gate)
            {
                return [.. _retrieved];
            }
        }
    }

    public void RecordToolCall(ToolInvocation invocation)
    {
        lock (_gate)
        {
            _toolCalls.Add(invocation);
        }
    }

    public void RecordRetrieval(IReadOnlyList<KnowledgeSearchResult> results)
    {
        lock (_gate)
        {
            // The agent may search more than once per turn. Keeping the first occurrence keeps
            // citation numbers stable once they have been handed to the model.
            foreach (var result in results.Where(result => _seenChunks.Add(result.ChunkId)))
            {
                _retrieved.Add(result);
            }
        }
    }
}
