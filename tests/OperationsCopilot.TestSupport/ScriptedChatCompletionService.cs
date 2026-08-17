using System.Text;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Services;

namespace OperationsCopilot.TestSupport;

/// <summary>One step of a scripted model turn: either call a tool, or reply with text.</summary>
public abstract record ScriptedStep
{
    /// <summary>The model decides to call <paramref name="FunctionName"/> on <paramref name="PluginName"/>.</summary>
    public sealed record CallTool(
        string PluginName,
        string FunctionName,
        Dictionary<string, object?>? Arguments = null) : ScriptedStep;

    /// <summary>The model produces its final answer and the turn ends.</summary>
    public sealed record Reply(string Text) : ScriptedStep;
}

/// <summary>
/// A chat completion service that follows a fixed script instead of calling a model.
/// </summary>
/// <remarks>
/// <para>
/// Semantic Kernel performs automatic function invocation inside the connector, not in the
/// kernel, so a fake has to run the tool itself for the pipeline to behave realistically. This
/// one invokes through <see cref="Kernel.InvokeAsync(KernelFunction, KernelArguments?, CancellationToken)"/>,
/// which means filters run, the recorder is populated, and citations flow exactly as they do in
/// production — with the model's judgement replaced by something a test can assert on.
/// </para>
/// <para>
/// It also captures the tool catalogue it was offered, which is what the offline tool-selection
/// evaluations inspect.
/// </para>
/// </remarks>
public sealed class ScriptedChatCompletionService(params ScriptedStep[] script) : IChatCompletionService
{
    private readonly Queue<ScriptedStep> _script = new(script);

    public IReadOnlyDictionary<string, object?> Attributes { get; } =
        new Dictionary<string, object?> { [AIServiceExtensions.ModelIdKey] = "scripted-test-model" };

    /// <summary>Tool metadata the kernel offered on the most recent call, in the order the kernel returned it.</summary>
    public IReadOnlyList<KernelFunctionMetadata> OfferedTools { get; private set; } = [];

    /// <summary>Fully qualified names of the tools this service actually invoked.</summary>
    public IReadOnlyList<string> InvokedTools => _invoked;

    /// <summary>The chat history of the most recent call, for asserting on prompt construction.</summary>
    public ChatHistory? LastHistory { get; private set; }

    private readonly List<string> _invoked = [];

    public async Task<IReadOnlyList<ChatMessageContent>> GetChatMessageContentsAsync(
        ChatHistory chatHistory,
        PromptExecutionSettings? executionSettings = null,
        Kernel? kernel = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(kernel);

        LastHistory = chatHistory;
        OfferedTools = [.. kernel.Plugins.GetFunctionsMetadata()];

        var toolOutput = new StringBuilder();

        while (_script.Count > 0)
        {
            switch (_script.Dequeue())
            {
                case ScriptedStep.CallTool call:
                    var result = await InvokeToolAsync(kernel, call, cancellationToken);
                    toolOutput.AppendLine(result);
                    break;

                case ScriptedStep.Reply reply:
                    return [new ChatMessageContent(AuthorRole.Assistant, reply.Text)];
            }
        }

        // No explicit Reply in the script: echo what the tools returned so the test still sees
        // the tool output travel end to end.
        return [new ChatMessageContent(AuthorRole.Assistant, toolOutput.ToString().Trim())];
    }

    private async Task<string> InvokeToolAsync(
        Kernel kernel,
        ScriptedStep.CallTool call,
        CancellationToken cancellationToken)
    {
        var function = kernel.Plugins.GetFunction(call.PluginName, call.FunctionName);
        var arguments = new KernelArguments(call.Arguments ?? []);

        _invoked.Add($"{call.PluginName}.{call.FunctionName}");

        var result = await kernel.InvokeAsync(function, arguments, cancellationToken);

        return result.GetValue<string>() ?? string.Empty;
    }

    public IAsyncEnumerable<StreamingChatMessageContent> GetStreamingChatMessageContentsAsync(
        ChatHistory chatHistory,
        PromptExecutionSettings? executionSettings = null,
        Kernel? kernel = null,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException(
            "The scripted service is non-streaming; ChatCompletionAgent.InvokeAsync does not stream.");
}
