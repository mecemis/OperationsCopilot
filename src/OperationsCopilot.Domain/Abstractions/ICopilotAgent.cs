using OperationsCopilot.Domain.Chat;

namespace OperationsCopilot.Domain.Abstractions;

/// <summary>The single agent behind <c>POST /api/chat</c>.</summary>
public interface ICopilotAgent
{
    Task<ChatResponse> AskAsync(ChatRequest request, CancellationToken cancellationToken = default);
}
