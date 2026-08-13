using System.ComponentModel.DataAnnotations;

namespace OperationsCopilot.Domain.Chat;

/// <summary>A single user turn sent to <c>POST /api/chat</c>.</summary>
/// <param name="Message">The user's question.</param>
/// <param name="ConversationId">
/// Opaque id returned by a previous response. Omit to start a new conversation;
/// pass it back to give the agent the earlier turns as context.
/// </param>
public sealed record ChatRequest(
    [property: Required]
    [property: StringLength(2000, MinimumLength = 1)]
    string Message,
    string? ConversationId = null);
