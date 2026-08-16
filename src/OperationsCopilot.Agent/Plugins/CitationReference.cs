using OperationsCopilot.Domain.Knowledge;

namespace OperationsCopilot.Agent.Plugins;

/// <summary>
/// Assigns the <c>[1]</c>, <c>[2]</c> markers that tie a sentence in the answer to a citation in
/// the response. Numbering follows first-retrieval order across the whole request, so a second
/// search in the same turn continues the sequence instead of restarting it.
/// </summary>
internal static class CitationReference
{
    public static string For(IReadOnlyList<KnowledgeSearchResult> retrieved, Guid chunkId)
    {
        var index = 0;
        for (var i = 0; i < retrieved.Count; i++)
        {
            if (retrieved[i].ChunkId == chunkId)
            {
                index = i + 1;
                break;
            }
        }

        return $"[{index}]";
    }

    public static string At(int zeroBasedIndex) => $"[{zeroBasedIndex + 1}]";
}
