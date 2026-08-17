using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;
using OperationsCopilot.Agent;
using OperationsCopilot.Agent.Plugins;
using OperationsCopilot.TestSupport;
using Shouldly;
using Xunit;

namespace OperationsCopilot.EvaluationTests.Tools;

/// <summary>
/// Evaluates the tool catalogue itself, offline.
/// </summary>
/// <remarks>
/// The catalogue is the entire basis on which the model decides what to call: it sees names,
/// descriptions and parameter descriptions, and nothing else. A tool with a vague description or
/// an undocumented parameter is the most common cause of wrong tool selection, and unlike model
/// behaviour it can be checked deterministically and for free on every commit.
/// </remarks>
[Collection(DatabaseCollection.Name)]
public class ToolCatalogueEvaluationTests(SeededDatabaseFixture fixture)
{
    /// <summary>Short descriptions do not give the model enough to discriminate between tools.</summary>
    private const int MinimumDescriptionLength = 60;

    private const int MinimumParameterDescriptionLength = 20;

    private static readonly string[] ExpectedTools =
    [
        ToolNames.GetLowStockProducts,
        ToolNames.GetSalesSummary,
        ToolNames.GetProductDetails,
        ToolNames.SearchKnowledgeBase,
    ];

    [Fact]
    public void Kernel_ExposesExactlyTheFourDocumentedTools()
    {
        var tools = GetToolMetadata();

        tools.Select(t => t.Name).OrderBy(name => name, StringComparer.Ordinal)
            .ShouldBe(ExpectedTools.OrderBy(name => name, StringComparer.Ordinal));
    }

    [Fact]
    public void Kernel_GroupsToolsUnderTheExpectedPlugins()
    {
        var tools = GetToolMetadata().ToDictionary(t => t.Name, t => t.PluginName, StringComparer.Ordinal);

        tools[ToolNames.GetLowStockProducts].ShouldBe(AgentServiceCollectionExtensions.OperationsPluginName);
        tools[ToolNames.GetSalesSummary].ShouldBe(AgentServiceCollectionExtensions.OperationsPluginName);
        tools[ToolNames.GetProductDetails].ShouldBe(AgentServiceCollectionExtensions.OperationsPluginName);
        tools[ToolNames.SearchKnowledgeBase].ShouldBe(AgentServiceCollectionExtensions.KnowledgePluginName);
    }

    [Fact]
    public void EveryTool_HasADescriptionSubstantialEnoughToChooseBy()
    {
        foreach (var tool in GetToolMetadata())
        {
            tool.Description.ShouldNotBeNullOrWhiteSpace($"{tool.Name} has no description.");
            tool.Description.Length.ShouldBeGreaterThanOrEqualTo(
                MinimumDescriptionLength,
                $"{tool.Name}'s description is too terse for the model to choose it reliably.");
        }
    }

    [Fact]
    public void EveryToolParameter_IsDescribed()
    {
        foreach (var tool in GetToolMetadata())
        {
            foreach (var parameter in tool.Parameters)
            {
                parameter.Description.ShouldNotBeNullOrWhiteSpace(
                    $"{tool.Name}.{parameter.Name} has no description, so the model must guess at its values.");

                parameter.Description!.Length.ShouldBeGreaterThanOrEqualTo(
                    MinimumParameterDescriptionLength,
                    $"{tool.Name}.{parameter.Name} is described too thinly to be filled in correctly.");
            }
        }
    }

    [Fact]
    public void FilterTools_AreOptionalSoABroadQuestionStillWorks()
    {
        var lowStock = GetToolMetadata().Single(t => t.Name == ToolNames.GetLowStockProducts);

        // Only mandatory arguments should be genuinely mandatory: forcing a warehouse code
        // would make "what's running low?" unanswerable without an invented value.
        lowStock.Parameters
            .Where(p => p.IsRequired)
            .ShouldBeEmpty();
    }

    [Fact]
    public void ProductLookup_RequiresTheOneArgumentItCannotDefault()
    {
        var details = GetToolMetadata().Single(t => t.Name == ToolNames.GetProductDetails);

        details.Parameters.Single(p => p.Name == "skuOrName").IsRequired.ShouldBeTrue();
    }

    [Fact]
    public void ToolDescriptions_NameTheConcreteValuesTheModelHasToSupply()
    {
        var tools = GetToolMetadata().ToDictionary(t => t.Name, StringComparer.Ordinal);

        var warehouse = tools[ToolNames.GetLowStockProducts].Parameters.Single(p => p.Name == "warehouseCode");
        warehouse.Description.ShouldContain("WH-EU-01");

        var region = tools[ToolNames.GetSalesSummary].Parameters.Single(p => p.Name == "region");
        region.Description.ShouldContain("EMEA");

        // Naming the documents is what stops the model answering a policy question from memory.
        tools[ToolNames.SearchKnowledgeBase].Description.ShouldContain("warranty");
    }

    private IReadOnlyList<KernelFunctionMetadata> GetToolMetadata()
    {
        using var scope = fixture.Services.CreateScope();

        // The kernel comes from the application's own registration, so this evaluates the tool
        // surface that actually ships rather than one assembled by the test.
        var kernel = scope.ServiceProvider.GetRequiredService<Kernel>();

        return [.. kernel.Plugins.GetFunctionsMetadata()];
    }
}
