namespace OperationsCopilot.Domain.Chat;

/// <summary>Who produced a stored conversation turn.</summary>
public enum ChatRole
{
    User,
    Assistant,
}

/// <summary>One stored turn of conversation history.</summary>
public sealed record ChatTurn(ChatRole Role, string Content, DateTimeOffset At);
