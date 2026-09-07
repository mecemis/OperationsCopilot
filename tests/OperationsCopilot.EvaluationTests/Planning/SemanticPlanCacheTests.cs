using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using OperationsCopilot.Domain.Abstractions;
using OperationsCopilot.Domain.Planning;
using OperationsCopilot.Infrastructure.Options;
using OperationsCopilot.Infrastructure.Persistence;
using OperationsCopilot.TestSupport;
using Shouldly;
using Xunit;

namespace OperationsCopilot.EvaluationTests.Planning;

/// <summary>
/// The plan cache against a real pgvector database.
/// </summary>
/// <remarks>
/// Runs on the deterministic embedding provider like the rest of the offline tier, which matches
/// on shared vocabulary rather than on meaning. That is enough to exercise everything structural
/// here — storage, the distance query, expiry, the day anchor, eviction — and it is deliberately
/// not enough to characterise the similarity floor, which depends entirely on the embedding
/// model. That measurement lives in <see cref="PlanCacheSeparationTests"/>.
/// </remarks>
[Collection(DatabaseCollection.Name)]
public class SemanticPlanCacheTests(SeededDatabaseFixture fixture)
{
    private static readonly DateOnly Today = new(2026, 9, 7);
    private static readonly DateTimeOffset Now = new(2026, 9, 7, 9, 0, 0, TimeSpan.Zero);

    private const string LowStockQuestion = "Which products are running low on stock?";

    [Fact]
    public async Task FindAsync_ReturnsThePlanStoredForTheSameQuestion()
    {
        var (cache, _) = await ArrangeAsync();

        await cache.StoreAsync(LowStockQuestion, LowStockPlan, Today, Cancellation);

        var hit = await cache.FindAsync(LowStockQuestion, Today, Cancellation);

        hit.ShouldNotBeNull();
        hit.MatchedQuestion.ShouldBe(LowStockQuestion);
        hit.Similarity.ShouldBeGreaterThanOrEqualTo(0.99);
        hit.TimesReused.ShouldBe(0);

        var call = hit.Plan.Calls.ShouldHaveSingleItem();
        call.Name.ShouldBe("Operations.GetLowStockProducts");
        call.Arguments["warehouseCode"].ShouldBe("WH-EU-01");
    }

    [Fact]
    public async Task FindAsync_ReturnsNothingForAnUnrelatedQuestion()
    {
        var (cache, _) = await ArrangeAsync();

        await cache.StoreAsync(LowStockQuestion, LowStockPlan, Today, Cancellation);

        var hit = await cache.FindAsync("What is the warranty period on safety helmets?", Today, Cancellation);

        hit.ShouldBeNull();
    }

    [Fact]
    public async Task FindAsync_RefusesAPlanAboutADifferentProductEvenWithNoSimilarityFloor()
    {
        // The floor is removed so that only one thing can reject this: the discriminator guard.
        // With a real embedding model these two questions score 0.87, well inside the band where
        // genuine paraphrases live, so this is the guard that has to hold.
        var (cache, _) = await ArrangeAsync(options => options.MinimumSimilarity = 0d);

        await cache.StoreAsync("Tell me about PT-1001.", ProductPlan("PT-1001"), Today, Cancellation);

        var hit = await cache.FindAsync("Tell me about PT-1006.", Today, Cancellation);

        hit.ShouldBeNull();
    }

    [Fact]
    public async Task FindAsync_RefusesAPlanForADifferentSalesWindow()
    {
        var (cache, _) = await ArrangeAsync(options => options.MinimumSimilarity = 0d);

        await cache.StoreAsync(
            "How much revenue did we make in the last 30 days?",
            SalesPlan(("lastDays", "30")),
            Today,
            Cancellation);

        var hit = await cache.FindAsync("How much revenue did we make in the last 90 days?", Today, Cancellation);

        hit.ShouldBeNull();
    }

    [Fact]
    public async Task FindAsync_IgnoresAPlanThatHasExpired()
    {
        var clock = new FakeTimeProvider(Now);
        var (cache, _) = await ArrangeAsync(options => options.TimeToLiveMinutes = 60, clock);

        await cache.StoreAsync(LowStockQuestion, LowStockPlan, Today, Cancellation);
        clock.Advance(TimeSpan.FromMinutes(61));

        var hit = await cache.FindAsync(LowStockQuestion, Today, Cancellation);

        hit.ShouldBeNull();
    }

    [Fact]
    public async Task FindAsync_OffersADateAnchoredPlanOnlyOnTheDayItWasMade()
    {
        var (cache, _) = await ArrangeAsync();
        const string question = "How did sales look last month?";

        // The model resolved "last month" into fixed bounds using the date it was told. Tomorrow
        // the same question means a different period, so the plan must not survive the day.
        await cache.StoreAsync(
            question,
            SalesPlan(("startDate", "2026-08-01"), ("endDate", "2026-08-31")),
            Today,
            Cancellation);

        (await cache.FindAsync(question, Today, Cancellation)).ShouldNotBeNull();
        (await cache.FindAsync(question, Today.AddDays(1), Cancellation)).ShouldBeNull();
    }

    [Fact]
    public async Task FindAsync_OffersAPlanWithNoDateArgumentOnAnyDay()
    {
        var (cache, _) = await ArrangeAsync();

        await cache.StoreAsync(LowStockQuestion, LowStockPlan, Today, Cancellation);

        (await cache.FindAsync(LowStockQuestion, Today.AddDays(30), Cancellation)).ShouldNotBeNull();
    }

    [Fact]
    public async Task StoreAsync_ReplacesANearDuplicateRatherThanAccumulatingWordings()
    {
        var (cache, services) = await ArrangeAsync();

        await cache.StoreAsync(LowStockQuestion, LowStockPlan, Today, Cancellation);
        await cache.StoreAsync(LowStockQuestion, LowStockPlan, Today, Cancellation);

        (await CountAsync(services)).ShouldBe(1);
    }

    [Fact]
    public async Task StoreAsync_KeepsTheReusedPlanWhenTheCacheIsFull()
    {
        var clock = new FakeTimeProvider(Now);
        var (cache, services) = await ArrangeAsync(
            options =>
            {
                options.MaxEntries = 10;
                // Unrelated questions here, but the floor is raised anyway so that near-duplicate
                // replacement cannot be what removes an entry — eviction is what is under test.
                options.MinimumSimilarity = 0.999;
            },
            clock);

        var questions = new[]
        {
            "alpha", "bravo", "charlie", "delta", "echo",
            "foxtrot", "golf", "hotel", "india", "juliet",
        };

        foreach (var question in questions)
        {
            await cache.StoreAsync(question, LowStockPlan, Today, Cancellation);
            clock.Advance(TimeSpan.FromSeconds(1));
        }

        // "alpha" is both the oldest and, until now, unused. Replaying it should be what saves it.
        var alpha = await cache.FindAsync("alpha", Today, Cancellation);
        await cache.RecordReuseAsync(alpha.ShouldNotBeNull().EntryId, Cancellation);
        clock.Advance(TimeSpan.FromSeconds(1));

        await cache.StoreAsync("kilo", LowStockPlan, Today, Cancellation);

        (await CountAsync(services)).ShouldBe(10);
        (await cache.FindAsync("alpha", Today, Cancellation)).ShouldNotBeNull();
        (await cache.FindAsync("bravo", Today, Cancellation)).ShouldBeNull();
    }

    [Fact]
    public async Task RecordReuseAsync_CountsTheReplayForTheNextLookup()
    {
        var (cache, _) = await ArrangeAsync();

        await cache.StoreAsync(LowStockQuestion, LowStockPlan, Today, Cancellation);
        var first = await cache.FindAsync(LowStockQuestion, Today, Cancellation);

        await cache.RecordReuseAsync(first.ShouldNotBeNull().EntryId, Cancellation);

        var second = await cache.FindAsync(LowStockQuestion, Today, Cancellation);

        second.ShouldNotBeNull().TimesReused.ShouldBe(1);
    }

    [Fact]
    public async Task RemoveAsync_DropsAPlanThatCanNoLongerBeReplayed()
    {
        var (cache, services) = await ArrangeAsync();

        await cache.StoreAsync(LowStockQuestion, LowStockPlan, Today, Cancellation);
        var hit = await cache.FindAsync(LowStockQuestion, Today, Cancellation);

        await cache.RemoveAsync(hit.ShouldNotBeNull().EntryId, Cancellation);

        (await CountAsync(services)).ShouldBe(0);
    }

    [Fact]
    public async Task StoreAsync_IgnoresATurnThatCalledNoTools()
    {
        var (cache, services) = await ArrangeAsync();

        await cache.StoreAsync("Hello there.", new ToolPlan([]), Today, Cancellation);

        (await CountAsync(services)).ShouldBe(0);
    }

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    private static ToolPlan LowStockPlan => new([
        new PlannedToolCall(
            "Operations",
            "GetLowStockProducts",
            new Dictionary<string, string?> { ["warehouseCode"] = "WH-EU-01" }),
    ]);

    private static ToolPlan ProductPlan(string sku) => new([
        new PlannedToolCall(
            "Operations",
            "GetProductDetails",
            new Dictionary<string, string?> { ["skuOrName"] = sku }),
    ]);

    private static ToolPlan SalesPlan(params (string Name, string? Value)[] arguments) => new([
        new PlannedToolCall(
            "Operations",
            "GetSalesSummary",
            arguments.ToDictionary(argument => argument.Name, argument => argument.Value)),
    ]);

    /// <summary>
    /// Builds a provider with the options this test needs and an empty cache table.
    /// </summary>
    /// <remarks>
    /// The table is shared by every test in the collection, so each one clears it first. The
    /// alternative — a database per test — would cost more than the isolation is worth here.
    /// </remarks>
    private async Task<(ISemanticPlanCache Cache, IServiceProvider Services)> ArrangeAsync(
        Action<SemanticCacheOptions>? configure = null,
        TimeProvider? clock = null)
    {
        var services = fixture.BuildServices(collection =>
        {
            collection.Configure<SemanticCacheOptions>(options => configure?.Invoke(options));

            if (clock is not null)
            {
                collection.AddSingleton(clock);
            }
        });

        await ClearAsync(services);

        // Resolved from a scope that outlives the test body: the cache holds a DbContext, and
        // every call in one test shares it exactly as one request would.
        return (services.CreateScope().ServiceProvider.GetRequiredService<ISemanticPlanCache>(), services);
    }

    private static async Task ClearAsync(IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();

        await scope.ServiceProvider.GetRequiredService<OperationsDbContext>()
            .CachedToolPlans
            .ExecuteDeleteAsync(Cancellation);
    }

    private static async Task<int> CountAsync(IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();

        return await scope.ServiceProvider.GetRequiredService<OperationsDbContext>()
            .CachedToolPlans
            .CountAsync(Cancellation);
    }
}
