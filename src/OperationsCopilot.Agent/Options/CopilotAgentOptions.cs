using System.ComponentModel.DataAnnotations;

namespace OperationsCopilot.Agent.Options;

/// <summary>Agent behaviour settings, bound from the <c>Agent</c> configuration section.</summary>
public sealed class CopilotAgentOptions
{
    public const string SectionName = "Agent";

    public string Name { get; set; } = "OperationsCopilot";

    /// <summary>Low by default: this agent reports figures from tools, where variation is a defect, not a feature.</summary>
    [Range(0d, 2d)]
    public double Temperature { get; set; } = 0.1;

    [Range(64, 8000)]
    public int MaxOutputTokens { get; set; } = 1200;

    /// <summary>
    /// Cap on tool invocations in a single turn, enforced by
    /// <c>ToolCallTrackingFilter</c>. Bounds cost and latency when the model gets stuck calling
    /// the same tool with slightly different arguments.
    /// </summary>
    [Range(1, 20)]
    public int MaxToolCallsPerTurn { get; set; } = 8;

    /// <summary>
    /// Extra instructions appended to the built-in system prompt. Lets a deployment add local
    /// rules without forking the prompt in source.
    /// </summary>
    public string? AdditionalInstructions { get; set; }
}
