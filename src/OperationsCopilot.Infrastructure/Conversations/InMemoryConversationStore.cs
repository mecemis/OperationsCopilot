using System.Collections.Concurrent;
using OperationsCopilot.Domain.Abstractions;
using OperationsCopilot.Domain.Chat;

namespace OperationsCopilot.Infrastructure.Conversations;

/// <summary>
/// Conversation history held in process memory, with a sliding expiry and a cap on turns.
/// </summary>
/// <remarks>
/// Deliberately not durable. History here is a convenience for follow-up questions, not a system
/// of record, and keeping it in memory avoids adding a cache dependency to a sample whose point
/// is RAG and tool calling. Behind a load balancer this needs sticky sessions, or a swap to a
/// distributed store — the interface is the seam for that.
/// </remarks>
public sealed class InMemoryConversationStore : IConversationStore
{
    /// <summary>Turns kept per conversation. Older turns fall out so the prompt cannot grow without bound.</summary>
    private const int MaxTurnsPerConversation = 12;

    private static readonly TimeSpan Lifetime = TimeSpan.FromHours(1);

    private readonly ConcurrentDictionary<string, Entry> _conversations = new(StringComparer.Ordinal);
    private readonly TimeProvider _timeProvider;

    public InMemoryConversationStore(TimeProvider timeProvider) => _timeProvider = timeProvider;

    public Task<IReadOnlyList<ChatTurn>> GetHistoryAsync(
        string conversationId,
        CancellationToken cancellationToken = default)
    {
        Evict();

        if (!_conversations.TryGetValue(conversationId, out var entry))
        {
            return Task.FromResult<IReadOnlyList<ChatTurn>>([]);
        }

        lock (entry.Gate)
        {
            entry.LastAccessed = _timeProvider.GetUtcNow();
            return Task.FromResult<IReadOnlyList<ChatTurn>>([.. entry.Turns]);
        }
    }

    public Task AppendAsync(
        string conversationId,
        IReadOnlyList<ChatTurn> turns,
        CancellationToken cancellationToken = default)
    {
        if (turns.Count == 0)
        {
            return Task.CompletedTask;
        }

        var entry = _conversations.GetOrAdd(conversationId, _ => new Entry(_timeProvider.GetUtcNow()));

        lock (entry.Gate)
        {
            entry.Turns.AddRange(turns);
            if (entry.Turns.Count > MaxTurnsPerConversation)
            {
                entry.Turns.RemoveRange(0, entry.Turns.Count - MaxTurnsPerConversation);
            }

            entry.LastAccessed = _timeProvider.GetUtcNow();
        }

        Evict();
        return Task.CompletedTask;
    }

    private void Evict()
    {
        var cutoff = _timeProvider.GetUtcNow() - Lifetime;

        foreach (var (id, entry) in _conversations)
        {
            if (entry.LastAccessed < cutoff)
            {
                _conversations.TryRemove(id, out _);
            }
        }
    }

    private sealed class Entry(DateTimeOffset createdAt)
    {
        public object Gate { get; } = new();

        public List<ChatTurn> Turns { get; } = [];

        public DateTimeOffset LastAccessed { get; set; } = createdAt;
    }
}
