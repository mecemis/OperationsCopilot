using Microsoft.SemanticKernel;
using OperationsCopilot.Agent.Planning;
using Shouldly;
using Xunit;

namespace OperationsCopilot.UnitTests.Planning;

/// <summary>
/// Pins the one Semantic Kernel behaviour the replay path silently depends on.
/// </summary>
/// <remarks>
/// If <see cref="Kernel.Clone"/> ever shared its plugin collection with the original instead of
/// copying it, clearing the copy would strip the tools from the live kernel in the middle of a
/// request. Nothing would throw. The agent would simply answer without calling anything, which
/// is the failure mode this whole project is built to prevent.
/// </remarks>
public class KernelExtensionsTests
{
    [Fact]
    public void WithoutTools_LeavesTheOriginalKernelsPluginsIntact()
    {
        var kernel = BuildKernel();

        var toolFree = kernel.WithoutTools();

        toolFree.Plugins.ShouldBeEmpty();
        kernel.Plugins.GetFunctionsMetadata().Select(f => f.Name).ShouldBe(["Probe"]);
    }

    [Fact]
    public void WithoutTools_KeepsTheServiceProvider()
    {
        // The clone still has to resolve the chat service and everything else the turn needs.
        var kernel = BuildKernel();

        kernel.WithoutTools().Services.ShouldBeSameAs(kernel.Services);
    }

    private static Kernel BuildKernel()
    {
        var kernel = new Kernel();

        kernel.Plugins.AddFromFunctions(
            "Test",
            [KernelFunctionFactory.CreateFromMethod(() => "ok", "Probe")]);

        return kernel;
    }
}
