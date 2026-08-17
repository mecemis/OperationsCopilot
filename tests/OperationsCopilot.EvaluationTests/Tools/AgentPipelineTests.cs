using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel.ChatCompletion;
using OperationsCopilot.Agent;
using OperationsCopilot.Agent.Plugins;
using OperationsCopilot.Domain.Abstractions;
using OperationsCopilot.Domain.Chat;
using OperationsCopilot.TestSupport;
using Shouldly;
using Xunit;

namespace OperationsCopilot.EvaluationTests.Tools;

/// <summary>
/// Exercises the whole request path with a scripted model in place of Azure OpenAI.
/// </summary>
/// <remarks>
/// Everything but the model's judgement is real here: real plugins, real database, real pgvector
/// search, real filter, real citation numbering. That makes these deterministic and free, while
/// still catching the failures that actually break the product — a tool that silently returns
/// nothing, citations that do not line up with the answer, a recorder that leaks between
/// requests. Whether the model picks the right tool is measured separately, in
/// <see cref="LiveToolSelectionEvaluationTests"/>.
/// </remarks>
[Collection(DatabaseCollection.Name)]
public class AgentPipelineTests(SeededDatabaseFixture fixture)
{
    [Fact]
    public async Task AskAsync_ReportsTheToolItCalled()
    {
        var response = await AskAsync(
            "Which products are running low?",
            new ScriptedStep.CallTool(
                AgentServiceCollectionExtensions.OperationsPluginName,
                ToolNames.GetLowStockProducts),
            new ScriptedStep.Reply("Four products are below their reorder point."));

        var call = response.ToolCalls.ShouldHaveSingleItem();

        call.Name.ShouldBe($"{AgentServiceCollectionExtensions.OperationsPluginName}.{ToolNames.GetLowStockProducts}");
        call.Succeeded.ShouldBeTrue();
    }

    [Fact]
    public async Task AskAsync_ReportsLatencyForTheTurnAndForEachTool()
    {
        var response = await AskAsync(
            "How did we do last month?",
            new ScriptedStep.CallTool(
                AgentServiceCollectionExtensions.OperationsPluginName,
                ToolNames.GetSalesSummary),
            new ScriptedStep.Reply("Revenue was steady."));

        response.LatencyMs.ShouldBeGreaterThan(0);
        response.ToolCalls.ShouldAllBe(call => call.DurationMs >= 0);
        // The turn cannot be quicker than the work inside it.
        response.LatencyMs.ShouldBeGreaterThanOrEqualTo(response.ToolCalls.Sum(c => c.DurationMs));
    }

    [Fact]
    public async Task AskAsync_ReturnsCitationsForRetrievedPassages()
    {
        var response = await AskAsync(
            "What is the restocking fee?",
            new ScriptedStep.CallTool(
                AgentServiceCollectionExtensions.KnowledgePluginName,
                ToolNames.SearchKnowledgeBase,
                new Dictionary<string, object?> { ["query"] = "restocking fee on returned goods" }),
            new ScriptedStep.Reply("Opened goods carry a 15% restocking fee [1]."));

        response.Citations.ShouldNotBeEmpty();

        var citation = response.Citations[0];
        citation.Reference.ShouldBe("[1]");
        citation.SourceFile.ShouldBe("returns-and-warranty-policy.md");
        citation.Excerpt.ShouldNotBeNullOrWhiteSpace();
        citation.Score.ShouldBeInRange(0d, 1d);
    }

    [Fact]
    public async Task AskAsync_NumbersCitationsToMatchTheMarkersTheModelWasGiven()
    {
        var response = await AskAsync(
            "What is the restocking fee?",
            new ScriptedStep.CallTool(
                AgentServiceCollectionExtensions.KnowledgePluginName,
                ToolNames.SearchKnowledgeBase,
                new Dictionary<string, object?> { ["query"] = "restocking fee" }),
            new ScriptedStep.Reply("See [1]."));

        // A [2] in the answer must resolve to citation 2 in the payload, or the citations are
        // worse than useless: they look authoritative while pointing at the wrong text.
        response.Citations
            .Select(c => c.Reference)
            .ShouldBe(Enumerable.Range(1, response.Citations.Count).Select(i => $"[{i}]"));
    }

    [Fact]
    public async Task AskAsync_CombinesADatabaseToolAndTheKnowledgeBaseInOneTurn()
    {
        var response = await AskAsync(
            "Which products need reordering, and how much should I order?",
            new ScriptedStep.CallTool(
                AgentServiceCollectionExtensions.OperationsPluginName,
                ToolNames.GetLowStockProducts),
            new ScriptedStep.CallTool(
                AgentServiceCollectionExtensions.KnowledgePluginName,
                ToolNames.SearchKnowledgeBase,
                new Dictionary<string, object?> { ["query"] = "how much to order when stock is low" }),
            new ScriptedStep.Reply("Order back to the threshold plus one lead-time cycle [1]."));

        // This is the combination the product exists for: live data plus the rule that governs it.
        response.ToolCalls.Select(c => c.FunctionName)
            .ShouldBe([ToolNames.GetLowStockProducts, ToolNames.SearchKnowledgeBase]);

        response.Citations.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task AskAsync_ReturnsNoCitationsWhenOnlyDatabaseToolsWereUsed()
    {
        var response = await AskAsync(
            "Tell me about PT-1001.",
            new ScriptedStep.CallTool(
                AgentServiceCollectionExtensions.OperationsPluginName,
                ToolNames.GetProductDetails,
                new Dictionary<string, object?> { ["skuOrName"] = "PT-1001" }),
            new ScriptedStep.Reply("PT-1001 is the Torqline 18V Brushless Drill."));

        // Live figures are not documents. Citing them would misrepresent where they came from.
        response.Citations.ShouldBeEmpty();
    }

    [Fact]
    public async Task AskAsync_StartsAConversationAndReturnsItsId()
    {
        var response = await AskAsync("Hello", new ScriptedStep.Reply("Hello."));

        response.ConversationId.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task AskAsync_CarriesEarlierTurnsIntoASecondQuestion()
    {
        var scripted = new ScriptedChatCompletionService(
            new ScriptedStep.Reply("Four products are low."),
            new ScriptedStep.Reply("They are all Power Tools."));

        var services = BuildServices(scripted);

        var first = await AskAsync(services, new ChatRequest("Which products are low?"));
        await AskAsync(services, new ChatRequest("What category are they?", first.ConversationId));

        // The follow-up must arrive with the earlier exchange, or "they" refers to nothing.
        var history = scripted.LastHistory.ShouldNotBeNull();

        history.Any(m => m.Content == "Which products are low?").ShouldBeTrue();
        history.Any(m => m.Content == "Four products are low.").ShouldBeTrue();
    }

    [Fact]
    public async Task AskAsync_KeepsToolCallsOutOfOtherRequests()
    {
        var services = BuildServices(new ScriptedChatCompletionService(
            new ScriptedStep.CallTool(
                AgentServiceCollectionExtensions.OperationsPluginName,
                ToolNames.GetLowStockProducts),
            new ScriptedStep.Reply("Done."),
            new ScriptedStep.Reply("Nothing to look up.")));

        var withTool = await AskAsync(services, new ChatRequest("Which products are low?"));
        var withoutTool = await AskAsync(services, new ChatRequest("Say hello."));

        withTool.ToolCalls.Count.ShouldBe(1);
        // The recorder is scoped; a second request must start from an empty slate.
        withoutTool.ToolCalls.ShouldBeEmpty();
    }

    private async Task<ChatResponse> AskAsync(string message, params ScriptedStep[] script)
        => await AskAsync(BuildServices(new ScriptedChatCompletionService(script)), new ChatRequest(message));

    private static async Task<ChatResponse> AskAsync(IServiceProvider services, ChatRequest request)
    {
        await using var scope = services.CreateAsyncScope();

        return await scope.ServiceProvider
            .GetRequiredService<ICopilotAgent>()
            .AskAsync(request, TestContext.Current.CancellationToken);
    }

    private IServiceProvider BuildServices(ScriptedChatCompletionService scripted)
        => fixture.BuildServices(services => services.AddSingleton<IChatCompletionService>(scripted));
}
