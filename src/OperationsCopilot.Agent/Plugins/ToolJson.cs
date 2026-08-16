using System.Text.Json;
using System.Text.Json.Serialization;

namespace OperationsCopilot.Agent.Plugins;

/// <summary>
/// Shared serialization for tool results.
/// </summary>
/// <remarks>
/// Tools hand the model JSON strings rather than returning objects for Semantic Kernel to
/// serialize. Doing it here keeps the token budget under our control, guarantees the model sees
/// camelCase field names that read like the prompt, and lets an empty result say so in words
/// instead of arriving as a bare <c>[]</c> the model has to interpret.
/// </remarks>
internal static class ToolJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
        WriteIndented = false,
    };

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);
}
