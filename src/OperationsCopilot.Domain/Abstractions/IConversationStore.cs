using OperationsCopilot.Domain.Chat;

namespace OperationsCopilot.Domain.Abstractions;

/// <summary>Short-lived conversation history so follow-up questions keep their context.</summary>
public interface IConversationStore
{
    /// <returns>Turns oldest first, or an empty list for an unknown conversation.</returns>
    Task<IReadOnlyList<ChatTurn>> GetHistoryAsync(
        string conversationId,
        CancellationToken cancellationToken = default);

    Task AppendAsync(
        string conversationId,
        IReadOnlyList<ChatTurn> turns,
        CancellationToken cancellationToken = default);
}
