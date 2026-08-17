using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using OperationsCopilot.Agent;
using OperationsCopilot.Agent.Filters;
using OperationsCopilot.Agent.Options;
using Shouldly;
using Xunit;

namespace OperationsCopilot.UnitTests.Agent;

public class ToolCallTrackingFilterTests
{
    private readonly ToolCallRecorder _recorder = new();

    [Fact]
    public async Task OnFunctionInvocationAsync_RecordsASuccessfulCallWithItsArguments()
    {
        var kernel = BuildKernel(budget: 8, () => "ok");

        await kernel.InvokeAsync(
            kernel.Plugins.GetFunction("Test", "Probe"),
            new KernelArguments { ["warehouseCode"] = "WH-EU-01" },
            TestContext.Current.CancellationToken);

        var call = _recorder.ToolCalls.ShouldHaveSingleItem();

        call.Name.ShouldBe("Test.Probe");
        call.Succeeded.ShouldBeTrue();
        call.Arguments["warehouseCode"].ShouldBe("WH-EU-01");
        call.DurationMs.ShouldBeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task OnFunctionInvocationAsync_RecordsAFailureAndRethrows()
    {
        var kernel = BuildKernel(budget: 8, () => throw new InvalidOperationException("database is down"));

        await Should.ThrowAsync<Exception>(async () => await kernel.InvokeAsync(
            kernel.Plugins.GetFunction("Test", "Probe"),
            cancellationToken: TestContext.Current.CancellationToken));

        var call = _recorder.ToolCalls.ShouldHaveSingleItem();

        call.Succeeded.ShouldBeFalse();
        call.Error.ShouldNotBeNull().ShouldContain("database is down");
    }

    [Fact]
    public async Task OnFunctionInvocationAsync_TruncatesLongArgumentValues()
    {
        var kernel = BuildKernel(budget: 8, () => "ok");

        await kernel.InvokeAsync(
            kernel.Plugins.GetFunction("Test", "Probe"),
            new KernelArguments { ["query"] = new string('x', 500) },
            TestContext.Current.CancellationToken);

        var recorded = _recorder.ToolCalls.Single().Arguments["query"];

        recorded!.Length.ShouldBe(201);
        recorded.ShouldEndWith("…");
    }

    [Fact]
    public async Task OnFunctionInvocationAsync_StopsCallingToolsOnceTheBudgetIsSpent()
    {
        var invocations = 0;
        var kernel = BuildKernel(budget: 2, () => $"call {++invocations}");
        var probe = kernel.Plugins.GetFunction("Test", "Probe");

        for (var i = 0; i < 4; i++)
        {
            await kernel.InvokeAsync(probe, cancellationToken: TestContext.Current.CancellationToken);
        }

        // The tool body must stop running, but the calls should still return a usable result so
        // the model can finish its answer rather than fail the whole turn.
        invocations.ShouldBe(2);
        _recorder.ToolCalls.Count.ShouldBe(2);
    }

    [Fact]
    public async Task OnFunctionInvocationAsync_TellsTheModelWhyItWasCutOff()
    {
        var kernel = BuildKernel(budget: 1, () => "ok");
        var probe = kernel.Plugins.GetFunction("Test", "Probe");

        await kernel.InvokeAsync(probe, cancellationToken: TestContext.Current.CancellationToken);
        var blocked = await kernel.InvokeAsync(probe, cancellationToken: TestContext.Current.CancellationToken);

        blocked.GetValue<string>().ShouldNotBeNull().ShouldContain("budget");
    }

    private Kernel BuildKernel(int budget, Func<string> body)
    {
        var kernel = new Kernel();

        kernel.Plugins.AddFromFunctions("Test", [KernelFunctionFactory.CreateFromMethod(body, "Probe")]);

        kernel.FunctionInvocationFilters.Add(new ToolCallTrackingFilter(
            _recorder,
            Microsoft.Extensions.Options.Options.Create(new CopilotAgentOptions { MaxToolCallsPerTurn = budget }),
            NullLogger<ToolCallTrackingFilter>.Instance));

        return kernel;
    }
}
