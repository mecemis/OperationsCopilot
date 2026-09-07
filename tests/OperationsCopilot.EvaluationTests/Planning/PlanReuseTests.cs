using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel.ChatCompletion;
using OperationsCopilot.Agent;
using OperationsCopilot.Agent.Plugins;
using OperationsCopilot.Domain.Abstractions;
using OperationsCopilot.Domain.Chat;
using OperationsCopilot.Infrastructure.Options;
using OperationsCopilot.Infrastructure.Persistence;
using OperationsCopilot.TestSupport;
using Shouldly;
using Xunit;

namespace OperationsCopilot.EvaluationTests.Planning;

/// <summary>
/// Plan reuse through the whole request path, with a scripted model standing in for the real one.
/// </summary>
/// <remarks>
/// The scripted model makes the central claim testable. On a replayed turn its script contains no
/// tool call at all — only a reply — so any tool that appears in the response must have come from
/// the cached plan rather than from the model. That is the saving the feature exists for, stated
/// as an assertion instead of as a benchmark.
/// </remarks>
[Collection(DatabaseCollection.Name)]
public class PlanReuseTests(SeededDatabaseFixture fixture)
{
    private const string LowStockQuestion = "Which products are running low on stock?";

    [Fact]
    public async Task AskAsync_CachesThePlanTheModelGenerated()
    {
        var services = await ArrangeAsync();

        var response = await AskAsync(services, LowStockQuestion, LowStockScript());

        var cache = response.PlanCache.ShouldNotBeNull();
        cache.Hit.ShouldBeFalse();
        cache.Stored.ShouldBeTrue();
        cache.Note.ShouldBeNull();
    }

    [Fact]
    public async Task AskAsync_ReplaysTheCachedPlanWithoutTheModelChoosingToolsAgain()
    {
        var services = await ArrangeAsync();

        await AskAsync(services, LowStockQuestion, LowStockScript());

        // Note the script: a reply and nothing else. The model is given no opportunity to call a
        // tool on this turn.
        var replayed = new ScriptedChatCompletionService(
            new ScriptedStep.Reply("Four products are below their reorder point."));

        var response = await AskAsync(services, LowStockQuestion, replayed);

        var cache = response.PlanCache.ShouldNotBeNull();
        cache.Hit.ShouldBeTrue();
        cache.MatchedQuestion.ShouldBe(LowStockQuestion);
        cache.Similarity.ShouldNotBeNull().ShouldBeGreaterThanOrEqualTo(0.85);

        // The tool ran anyway, which it could only have done from the stored plan.
        response.ToolCalls.ShouldHaveSingleItem().FunctionName.ShouldBe(ToolNames.GetLowStockProducts);

        // And the tool catalogue was not sent. Skipping the round trip while still paying for the
        // tool definitions in the prompt would give up most of the saving.
        replayed.OfferedTools.ShouldBeEmpty();
        replayed.InvokedTools.ShouldBeEmpty();
    }

    [Fact]
    public async Task AskAsync_KeepsCitationsWorkingOnAReplayedTurn()
    {
        var services = await ArrangeAsync();
        const string question = "What is the restocking fee on returned goods?";

        await AskAsync(
            services,
            question,
            new ScriptedChatCompletionService(
                new ScriptedStep.CallTool(
                    AgentServiceCollectionExtensions.KnowledgePluginName,
                    ToolNames.SearchKnowledgeBase,
                    new Dictionary<string, object?> { ["query"] = "restocking fee on returned goods" }),
                new ScriptedStep.Reply("Opened goods carry a restocking fee [1].")));

        var response = await AskAsync(
            services,
            question,
            new ScriptedChatCompletionService(
                new ScriptedStep.Reply("Opened goods carry a restocking fee [1].")));

        response.PlanCache.ShouldNotBeNull().Hit.ShouldBeTrue();

        // Retrieval happened through the kernel, so the filter and the recorder saw it and the
        // answer's [1] still resolves to a real passage.
        response.Citations.ShouldNotBeEmpty();

        var citation = response.Citations[0];
        citation.Reference.ShouldBe("[1]");
        citation.SourceFile.ShouldBe("returns-and-warranty-policy.md");
    }

    [Fact]
    public async Task AskAsync_ReportsTheReplayCountBackToTheCaller()
    {
        var services = await ArrangeAsync();

        await AskAsync(services, LowStockQuestion, LowStockScript());
        await AskAsync(services, LowStockQuestion, Reply());
        var third = await AskAsync(services, LowStockQuestion, Reply());

        third.PlanCache.ShouldNotBeNull().TimesReused.ShouldBe(1);
    }

    [Fact]
    public async Task AskAsync_DoesNotCacheATurnThatCalledNoTools()
    {
        var services = await ArrangeAsync();

        var response = await AskAsync(services, "Hello there.", Reply("Hello."));

        var cache = response.PlanCache.ShouldNotBeNull();
        cache.Stored.ShouldBeFalse();
        cache.Note.ShouldNotBeNull().ShouldContain("without calling a tool");
    }

    [Fact]
    public async Task AskAsync_LeavesFollowUpTurnsToTheModel()
    {
        var services = await ArrangeAsync();

        var scripted = new ScriptedChatCompletionService(
            new ScriptedStep.Reply("Four products are low."),
            new ScriptedStep.Reply("They are all Power Tools."));

        var first = await AskAsync(services, new ChatRequest(LowStockQuestion), scripted);
        var second = await AskAsync(services, new ChatRequest("What category are they?", first.ConversationId), scripted);

        // "What category are they?" is not a question on its own, so neither storing it nor
        // matching against it would mean anything.
        var cache = second.PlanCache.ShouldNotBeNull();
        cache.Hit.ShouldBeFalse();
        cache.Stored.ShouldBeFalse();
        cache.Note.ShouldNotBeNull().ShouldContain("Follow-up");
    }

    [Fact]
    public async Task AskAsync_DiscardsAPlanNamingAToolThatNoLongerExists()
    {
        var services = await ArrangeAsync();

        // Store a plan by hand for a tool the kernel does not have, which is what a renamed tool
        // leaves behind in a cache that outlives a deploy.
        await using (var scope = services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<ISemanticPlanCache>().StoreAsync(
                LowStockQuestion,
                new Domain.Planning.ToolPlan([
                    new Domain.Planning.PlannedToolCall("Operations", "GetStockLevelsRenamed", new Dictionary<string, string?>()),
                ]),
                DateOnly.FromDateTime(DateTime.UtcNow),
                TestContext.Current.CancellationToken);
        }

        var response = await AskAsync(services, LowStockQuestion, LowStockScript());

        var cache = response.PlanCache.ShouldNotBeNull();
        cache.Hit.ShouldBeFalse();
        cache.Note.ShouldNotBeNull().ShouldContain("no longer exists");

        // The turn still answers, by planning it the normal way.
        response.ToolCalls.ShouldHaveSingleItem().FunctionName.ShouldBe(ToolNames.GetLowStockProducts);
    }

    private static ScriptedChatCompletionService LowStockScript() =>
        new(
            new ScriptedStep.CallTool(
                AgentServiceCollectionExtensions.OperationsPluginName,
                ToolNames.GetLowStockProducts),
            new ScriptedStep.Reply("Four products are below their reorder point."));

    private static ScriptedChatCompletionService Reply(string text = "Four products are below their reorder point.") =>
        new(new ScriptedStep.Reply(text));

    private static Task<ChatResponse> AskAsync(
        IServiceProvider services,
        string message,
        ScriptedChatCompletionService scripted)
        => AskAsync(services, new ChatRequest(message), scripted);

    /// <summary>
    /// Runs one turn against its own scripted model, resolved from a fresh scope the way a
    /// request would be.
    /// </summary>
    /// <remarks>
    /// The chat service is swapped per turn rather than per test because a cached turn and an
    /// uncached one call the model a different number of times, and a single shared script would
    /// make the tests about queue arithmetic instead of about caching.
    /// </remarks>
    private static async Task<ChatResponse> AskAsync(
        IServiceProvider services,
        ChatRequest request,
        ScriptedChatCompletionService scripted)
    {
        await using var scope = services.CreateAsyncScope();

        ScriptedChatServiceHolder.Current = scripted;

        return await scope.ServiceProvider
            .GetRequiredService<ICopilotAgent>()
            .AskAsync(request, TestContext.Current.CancellationToken);
    }

    /// <summary>Builds a provider with plan reuse switched on and an empty cache table.</summary>
    private async Task<IServiceProvider> ArrangeAsync()
    {
        var services = fixture.BuildServices(collection =>
        {
            collection.Configure<SemanticCacheOptions>(options => options.Enabled = true);
            collection.AddSingleton<IChatCompletionService>(_ => ScriptedChatServiceHolder.Require());
        });

        await using var scope = services.CreateAsyncScope();

        await scope.ServiceProvider.GetRequiredService<OperationsDbContext>()
            .CachedToolPlans
            .ExecuteDeleteAsync(TestContext.Current.CancellationToken);

        return services;
    }
}

/// <summary>
/// Lets a test swap the scripted model between turns of the same service provider.
/// </summary>
/// <remarks>
/// The provider is built once per test so that the cache persists across turns, but each turn
/// needs its own script. An async-local holder is the smallest thing that gives both, and it
/// keeps tests in this collection from seeing each other's scripts.
/// </remarks>
internal static class ScriptedChatServiceHolder
{
    private static readonly AsyncLocal<ScriptedChatCompletionService?> Value = new();

    public static ScriptedChatCompletionService? Current
    {
        get => Value.Value;
        set => Value.Value = value;
    }

    public static ScriptedChatCompletionService Require() =>
        Current ?? throw new InvalidOperationException("No scripted chat service was set for this turn.");
}
