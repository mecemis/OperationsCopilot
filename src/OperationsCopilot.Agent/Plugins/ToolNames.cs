namespace OperationsCopilot.Agent.Plugins;

/// <summary>
/// The names the model sees for each tool. Declared as constants so that the plugin
/// attributes and the tool-selection evaluations cannot drift apart: renaming a tool here is a
/// compile error in the tests rather than a silently failing expectation.
/// </summary>
public static class ToolNames
{
    public const string GetLowStockProducts = nameof(GetLowStockProducts);

    public const string GetSalesSummary = nameof(GetSalesSummary);

    public const string GetProductDetails = nameof(GetProductDetails);

    public const string SearchKnowledgeBase = nameof(SearchKnowledgeBase);
}
