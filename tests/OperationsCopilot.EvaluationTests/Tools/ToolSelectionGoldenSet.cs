using OperationsCopilot.Agent.Plugins;

namespace OperationsCopilot.EvaluationTests.Tools;

/// <summary>One labelled tool-selection case.</summary>
/// <param name="ExpectedTools">
/// Tools the agent must call to answer correctly. Missing any of these is a recall failure and
/// generally means the answer was guessed rather than looked up.
/// </param>
/// <param name="AcceptableExtraTools">
/// Tools that are reasonable but not required. Calling one of these is not counted against
/// precision; calling anything else is.
/// </param>
public sealed record ToolSelectionCase(
    string Question,
    string[] ExpectedTools,
    string[]? AcceptableExtraTools = null)
{
    public IReadOnlySet<string> Required => ExpectedTools.ToHashSet(StringComparer.Ordinal);

    public IReadOnlySet<string> Permitted =>
        ExpectedTools.Concat(AcceptableExtraTools ?? []).ToHashSet(StringComparer.Ordinal);
}

/// <summary>
/// The labelled question set for evaluating which tools the agent chooses.
/// </summary>
/// <remarks>
/// The interesting cases are the combined ones at the end: a question with a data half and a
/// policy half should produce calls to both a database tool and the knowledge base. Getting only
/// half of that is the most common and most damaging failure — the answer looks confident while
/// quietly making up the rule or the numbers.
/// </remarks>
public static class ToolSelectionGoldenSet
{
    public static readonly IReadOnlyList<ToolSelectionCase> Cases =
    [
        // --- Single-tool: database ---------------------------------------------------
        new("Which products are running low on stock?",
            [ToolNames.GetLowStockProducts]),

        new("What needs reordering in the Rotterdam warehouse?",
            [ToolNames.GetLowStockProducts]),

        new("How much revenue did we make in the last 30 days?",
            [ToolNames.GetSalesSummary]),

        new("What were our best selling categories last quarter?",
            [ToolNames.GetSalesSummary]),

        new("Tell me about PT-1001.",
            [ToolNames.GetProductDetails]),

        new("How many Guardline Hard Hat Vented do we have in stock?",
            [ToolNames.GetProductDetails],
            AcceptableExtraTools: [ToolNames.GetLowStockProducts]),

        // --- Single-tool: knowledge base ---------------------------------------------
        new("What is our returns policy?",
            [ToolNames.SearchKnowledgeBase]),

        new("Who needs to approve a 15% discount?",
            [ToolNames.SearchKnowledgeBase]),

        new("How is the reorder threshold calculated?",
            [ToolNames.SearchKnowledgeBase]),

        new("What is the warranty period for safety equipment?",
            [ToolNames.SearchKnowledgeBase]),

        // --- Combined: data plus policy ----------------------------------------------
        new("Which products need reordering, and how much should I order according to our policy?",
            [ToolNames.GetLowStockProducts, ToolNames.SearchKnowledgeBase]),

        new("PT-1006 is discontinued — how should I price the remaining stock?",
            [ToolNames.GetProductDetails, ToolNames.SearchKnowledgeBase]),

        new("Is the stock figure for EL-2002 still trustworthy under our cycle counting policy?",
            [ToolNames.GetProductDetails, ToolNames.SearchKnowledgeBase]),

        new("Are any low-stock items at the critical level our inventory policy defines?",
            [ToolNames.GetLowStockProducts, ToolNames.SearchKnowledgeBase]),
    ];
}
