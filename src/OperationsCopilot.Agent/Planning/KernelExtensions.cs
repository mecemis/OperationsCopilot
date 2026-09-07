using Microsoft.SemanticKernel;

namespace OperationsCopilot.Agent.Planning;

public static class KernelExtensions
{
    /// <summary>
    /// A copy of the kernel with no plugins, for a model call that must not be offered any tools.
    /// </summary>
    /// <remarks>
    /// Relied on by the replay path, where the tools were chosen on an earlier turn and sending
    /// their definitions again would give up most of what the plan cache saves.
    /// <para>
    /// This leans on <see cref="Kernel.Clone"/> giving the copy its own plugin collection. It
    /// does — but if that ever changed, clearing the copy would strip the tools from the live
    /// kernel mid-request and the agent would quietly stop being able to call anything.
    /// <c>KernelExtensionsTests</c> pins the behaviour so the change would surface as a failing
    /// test rather than as an agent that answers everything from memory.
    /// </para>
    /// </remarks>
    public static Kernel WithoutTools(this Kernel kernel)
    {
        var toolFree = kernel.Clone();
        toolFree.Plugins.Clear();

        return toolFree;
    }
}
