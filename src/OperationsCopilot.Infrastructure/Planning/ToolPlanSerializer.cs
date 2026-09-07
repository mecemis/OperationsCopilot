using System.Text.Json;
using OperationsCopilot.Domain.Planning;

namespace OperationsCopilot.Infrastructure.Planning;

/// <summary>
/// The one definition of how a plan is written to and read from the database.
/// </summary>
/// <remarks>
/// Shared by the EF value converter, which handles writes, and by the raw vector query, which
/// handles reads. Two copies of these options would be a bug waiting for the day someone changes
/// one of them.
/// </remarks>
internal static class ToolPlanSerializer
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static string Serialize(IReadOnlyList<PlannedToolCall> calls) =>
        JsonSerializer.Serialize(calls, Options);

    public static IReadOnlyList<PlannedToolCall> Deserialize(string json) =>
        JsonSerializer.Deserialize<IReadOnlyList<PlannedToolCall>>(json, Options) ?? [];
}
