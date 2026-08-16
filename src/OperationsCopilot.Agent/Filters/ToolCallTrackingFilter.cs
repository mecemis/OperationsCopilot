using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using OperationsCopilot.Agent.Options;
using OperationsCopilot.Domain.Abstractions;
using OperationsCopilot.Domain.Chat;

namespace OperationsCopilot.Agent.Filters;

/// <summary>
/// Records every kernel function the agent invokes, with its arguments and duration.
/// </summary>
/// <remarks>
/// A filter is the right seam for this: it sees every call the model makes without any tool
/// having to opt in, so the "tools used" list in the response cannot silently drift out of step
/// with what actually ran. A failing tool is recorded and rethrown, so the agent's own error
/// handling still applies.
/// </remarks>
public sealed class ToolCallTrackingFilter(
    IToolCallRecorder recorder,
    IOptions<CopilotAgentOptions> agentOptions,
    ILogger<ToolCallTrackingFilter> logger) : IFunctionInvocationFilter
{
    /// <summary>Argument values are truncated in the response; full values stay in the logs.</summary>
    private const int MaxArgumentLength = 200;

    private readonly CopilotAgentOptions _options = agentOptions.Value;

    public async Task OnFunctionInvocationAsync(
        FunctionInvocationContext context,
        Func<FunctionInvocationContext, Task> next)
    {
        if (recorder.ToolCalls.Count >= _options.MaxToolCallsPerTurn)
        {
            // Short-circuit rather than throw: the model gets a plain instruction to stop
            // calling tools and answer, so the user still gets a reply built on what was
            // gathered instead of an error.
            logger.LogWarning(
                "Tool call budget of {Budget} reached; refusing {Plugin}.{Function}.",
                _options.MaxToolCallsPerTurn,
                context.Function.PluginName,
                context.Function.Name);

            context.Result = new FunctionResult(
                context.Function,
                "The tool call budget for this turn is exhausted. Answer now using the " +
                "information already gathered, and say which part you could not verify.");

            return;
        }

        var stopwatch = Stopwatch.StartNew();

        try
        {
            await next(context);
            stopwatch.Stop();

            Record(context, stopwatch.ElapsedMilliseconds, succeeded: true, error: null);

            logger.LogInformation(
                "Tool {Plugin}.{Function} completed in {ElapsedMs}ms.",
                context.Function.PluginName,
                context.Function.Name,
                stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();

            Record(context, stopwatch.ElapsedMilliseconds, succeeded: false, error: ex.Message);

            logger.LogError(
                ex,
                "Tool {Plugin}.{Function} failed after {ElapsedMs}ms.",
                context.Function.PluginName,
                context.Function.Name,
                stopwatch.ElapsedMilliseconds);

            throw;
        }
    }

    private void Record(FunctionInvocationContext context, long elapsedMs, bool succeeded, string? error)
        => recorder.RecordToolCall(new ToolInvocation(
            context.Function.PluginName ?? "Unknown",
            context.Function.Name,
            DescribeArguments(context.Arguments),
            elapsedMs,
            succeeded,
            error));

    private static Dictionary<string, string?> DescribeArguments(KernelArguments arguments)
    {
        var described = new Dictionary<string, string?>(StringComparer.Ordinal);

        foreach (var (key, value) in arguments)
        {
            var text = value?.ToString();

            described[key] = text is { Length: > MaxArgumentLength }
                ? text[..MaxArgumentLength] + "…"
                : text;
        }

        return described;
    }
}
