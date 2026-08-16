using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.AzureOpenAI;
using OperationsCopilot.Agent.Options;
using OperationsCopilot.Agent.Plugins;
using OperationsCopilot.Domain.Abstractions;
using OperationsCopilot.Domain.Chat;

// Both OpenAI.Chat and Microsoft.SemanticKernel define ChatMessageContent; alias the one
// type needed from the OpenAI package rather than importing the whole namespace.
using ChatTokenUsage = OpenAI.Chat.ChatTokenUsage;

namespace OperationsCopilot.Agent;

/// <summary>
/// The single Semantic Kernel agent behind <c>POST /api/chat</c>.
/// </summary>
/// <remarks>
/// There is no orchestration logic here on purpose. The kernel is given four tools and
/// <see cref="FunctionChoiceBehavior.Auto()"/>, and the model decides which to call and in what
/// order — including calling a database tool and the knowledge base in the same turn and
/// combining them. This class's job is to assemble the request, and to turn what happened into
/// an auditable response: the answer, the passages retrieved, the tools invoked, and the timings.
/// </remarks>
public sealed class CopilotAgent(
    Kernel kernel,
    IToolCallRecorder recorder,
    IConversationStore conversationStore,
    IOptions<CopilotAgentOptions> agentOptions,
    TimeProvider timeProvider,
    ILogger<CopilotAgent> logger) : ICopilotAgent
{
    /// <summary>Citation excerpts are trimmed for display; the full passage stays in the database.</summary>
    private const int MaxExcerptLength = 400;

    private readonly CopilotAgentOptions _options = agentOptions.Value;

    public async Task<ChatResponse> AskAsync(
        ChatRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Message);

        var stopwatch = Stopwatch.StartNew();
        var conversationId = request.ConversationId is { Length: > 0 } id ? id : Guid.CreateVersion7().ToString("n");
        var askedAt = timeProvider.GetUtcNow();

        var thread = await BuildThreadAsync(conversationId, cancellationToken);
        var agent = BuildAgent();

        var answer = await InvokeAsync(agent, thread, request.Message, cancellationToken);

        stopwatch.Stop();

        await conversationStore.AppendAsync(
            conversationId,
            [
                new ChatTurn(ChatRole.User, request.Message, askedAt),
                new ChatTurn(ChatRole.Assistant, answer.Text, timeProvider.GetUtcNow()),
            ],
            cancellationToken);

        var toolCalls = recorder.ToolCalls;

        logger.LogInformation(
            "Answered conversation {ConversationId} in {LatencyMs}ms using {ToolCount} tool call(s) and {CitationCount} passage(s).",
            conversationId,
            stopwatch.ElapsedMilliseconds,
            toolCalls.Count,
            recorder.RetrievedPassages.Count);

        return new ChatResponse(
            answer.Text,
            conversationId,
            BuildCitations(),
            toolCalls,
            stopwatch.ElapsedMilliseconds)
        {
            Usage = answer.Usage,
        };
    }

    private ChatCompletionAgent BuildAgent()
    {
        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);

        return new ChatCompletionAgent
        {
            Name = _options.Name,
            Description = "Answers questions about Aurora Supply Co. stock, sales, products and policy.",
            Instructions = CopilotSystemPrompt.Build(today, _options.AdditionalInstructions),
            Kernel = kernel,
            Arguments = new KernelArguments(new AzureOpenAIPromptExecutionSettings
            {
                // Auto is the whole point of the demo: the model picks the tools, not us.
                FunctionChoiceBehavior = FunctionChoiceBehavior.Auto(),
                Temperature = _options.Temperature,
                MaxTokens = _options.MaxOutputTokens,
            }),
        };
    }

    private async Task<ChatHistoryAgentThread> BuildThreadAsync(
        string conversationId,
        CancellationToken cancellationToken)
    {
        var history = new ChatHistory();

        foreach (var turn in await conversationStore.GetHistoryAsync(conversationId, cancellationToken))
        {
            history.Add(new ChatMessageContent(
                turn.Role == ChatRole.User ? AuthorRole.User : AuthorRole.Assistant,
                turn.Content));
        }

        return new ChatHistoryAgentThread(history, conversationId);
    }

    private static async Task<AgentAnswer> InvokeAsync(
        ChatCompletionAgent agent,
        AgentThread thread,
        string message,
        CancellationToken cancellationToken)
    {
        var text = new List<string>();
        var promptTokens = 0;
        var completionTokens = 0;

        await foreach (var item in agent.InvokeAsync(message, thread, options: null, cancellationToken))
        {
            if (!string.IsNullOrWhiteSpace(item.Message.Content))
            {
                text.Add(item.Message.Content);
            }

            // Tool rounds each produce their own completion; usage has to be summed across them
            // or the reported cost of a multi-tool answer is understated.
            if (TryReadUsage(item.Message.Metadata, out var usage))
            {
                promptTokens += usage.InputTokenCount;
                completionTokens += usage.OutputTokenCount;
            }
        }

        var answer = text.Count > 0
            ? string.Join("\n\n", text).Trim()
            : "I could not produce an answer for that. Try rephrasing the question.";

        return new AgentAnswer(
            answer,
            promptTokens + completionTokens > 0 ? new TokenUsage(promptTokens, completionTokens) : null);
    }

    private static bool TryReadUsage(
        IReadOnlyDictionary<string, object?>? metadata,
        out ChatTokenUsage usage)
    {
        if (metadata is not null
            && metadata.TryGetValue("Usage", out var raw)
            && raw is ChatTokenUsage reported)
        {
            usage = reported;
            return true;
        }

        usage = null!;
        return false;
    }

    /// <summary>
    /// Turns the passages retrieved this turn into citations, numbered in the same order the
    /// model saw them so a <c>[2]</c> in the answer text resolves to citation 2 here.
    /// </summary>
    private IReadOnlyList<Citation> BuildCitations()
    {
        var passages = recorder.RetrievedPassages;

        return [.. passages.Select((passage, index) => new Citation(
            CitationReference.At(index),
            passage.SourceFile,
            passage.DocumentTitle,
            passage.Heading,
            Excerpt(passage.Content),
            Math.Round(passage.Score, 4)))];
    }

    private static string Excerpt(string content)
    {
        var normalized = content.ReplaceLineEndings(" ").Trim();

        return normalized.Length <= MaxExcerptLength
            ? normalized
            : normalized[..MaxExcerptLength].TrimEnd() + "…";
    }

    private sealed record AgentAnswer(string Text, TokenUsage? Usage);
}
