using System.ComponentModel;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using OperationsCopilot.Domain.Abstractions;
using OperationsCopilot.Infrastructure.Options;

namespace OperationsCopilot.Agent.Plugins;

/// <summary>
/// The retrieval tool. Search results are recorded on the request's
/// <see cref="IToolCallRecorder"/> as they are returned, which is what lets the API attach
/// citations to the answer without the plugin knowing anything about HTTP.
/// </summary>
public sealed class KnowledgeBasePlugin(
    IKnowledgeBaseSearch search,
    IToolCallRecorder recorder,
    IOptions<RagOptions> ragOptions)
{
    private readonly RagOptions _options = ragOptions.Value;

    [KernelFunction(ToolNames.SearchKnowledgeBase)]
    [Description(
        "Searches Aurora Supply Co.'s internal policy and product documents: the inventory and " +
        "replenishment policy, supplier management standard, returns and warranty policy, " +
        "pricing and discount policy, and product catalog guide. Use for questions about rules, " +
        "policies, thresholds, approval limits, warranty periods, lead times, or process. " +
        "Always use this rather than answering a policy question from memory.")]
    public async Task<string> SearchKnowledgeBaseAsync(
        [Description("What to look for, phrased as a natural-language question or topic. Use the user's own wording where possible.")]
        string query,
        [Description("Maximum passages to return. Defaults to the configured value; raise it for broad questions that span several documents.")]
        int? topK = null,
        CancellationToken cancellationToken = default)
    {
        var results = await search.SearchAsync(
            query,
            topK ?? _options.TopK,
            _options.MinimumScore,
            cancellationToken);

        if (results.Count == 0)
        {
            return "The knowledge base contains no passage relevant to that query. Say so rather than guessing at the policy.";
        }

        recorder.RecordRetrieval(results);
        var allRetrieved = recorder.RetrievedPassages;

        // The reference markers handed to the model here are the same ones the API puts on the
        // citations it returns, so a [2] in the answer text resolves to citation 2 in the payload.
        return ToolJson.Serialize(new
        {
            count = results.Count,
            passages = results.Select(r => new
            {
                reference = CitationReference.For(allRetrieved, r.ChunkId),
                r.DocumentTitle,
                r.Heading,
                source = r.SourceFile,
                score = Math.Round(r.Score, 4),
                r.Content,
            }),
        });
    }
}
