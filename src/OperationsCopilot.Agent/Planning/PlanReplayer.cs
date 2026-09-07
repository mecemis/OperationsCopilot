using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using OperationsCopilot.Domain.Planning;

namespace OperationsCopilot.Agent.Planning;

/// <summary>
/// Runs the tool calls of a cached plan and returns what they produced.
/// </summary>
/// <remarks>
/// <para>
/// Calls go through <see cref="Kernel.InvokeAsync(KernelFunction, KernelArguments?, CancellationToken)"/>
/// rather than straight to the plugin objects, which matters more than it looks: the invocation
/// filter still runs, so the tool call budget still applies, the recorder still fills, and
/// knowledge-base passages still become citations. A replayed turn is auditable in exactly the
/// same way as a planned one.
/// </para>
/// <para>
/// What is replayed is the choice of tools and their arguments. The tools themselves query live
/// data every time, so a reused plan cannot return a stale stock level — only a stale idea of
/// which stock level to look up.
/// </para>
/// </remarks>
public sealed class PlanReplayer(Kernel kernel, ILogger<PlanReplayer> logger)
{
    /// <summary>
    /// True when every tool the plan names still exists.
    /// </summary>
    /// <remarks>
    /// Checked before anything runs, so a plan left over from before a tool was renamed is
    /// discarded whole instead of half-executing and leaving a turn to explain itself.
    /// </remarks>
    public bool CanReplay(ToolPlan plan) =>
        plan.Calls.Count > 0 && plan.Calls.All(call => TryResolve(call, out _));

    /// <summary>Executes the plan in order.</summary>
    /// <returns>Each call's output, labelled with the tool that produced it.</returns>
    public async Task<IReadOnlyList<ReplayedCall>> ExecuteAsync(
        ToolPlan plan,
        CancellationToken cancellationToken = default)
    {
        var results = new List<ReplayedCall>(plan.Calls.Count);

        foreach (var call in plan.Calls)
        {
            if (!TryResolve(call, out var function))
            {
                // CanReplay is the gate for this; reaching here means the plugins changed
                // underneath us mid-turn, which is not worth failing the request over.
                logger.LogWarning("Skipping {Tool}: it is no longer registered.", call.Name);
                continue;
            }

            results.Add(new ReplayedCall(call.Name, await InvokeAsync(function, call, cancellationToken)));
        }

        return results;
    }

    private async Task<string> InvokeAsync(
        KernelFunction function,
        PlannedToolCall call,
        CancellationToken cancellationToken)
    {
        var arguments = new KernelArguments();

        // Values were stored as the strings the model produced. Semantic Kernel converts each
        // one to its parameter's type on the way in, the same conversion the live path makes.
        foreach (var (name, value) in call.Arguments)
        {
            arguments[name] = value;
        }

        try
        {
            var result = await kernel.InvokeAsync(function, arguments, cancellationToken);

            return result.GetValue<string>() ?? string.Empty;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // A tool that throws during normal operation has its message handed back to the model,
            // which then says what it could not verify. Replay behaves the same way rather than
            // turning a recoverable tool fault into a failed request.
            logger.LogError(exception, "Replayed tool {Tool} failed.", call.Name);

            return $"This tool failed and returned no data: {exception.Message}";
        }
    }

    private bool TryResolve(PlannedToolCall call, out KernelFunction function) =>
        kernel.Plugins.TryGetFunction(call.PluginName, call.FunctionName, out function!);
}

/// <summary>One replayed tool call and the output it returned.</summary>
public sealed record ReplayedCall(string ToolName, string Output);
