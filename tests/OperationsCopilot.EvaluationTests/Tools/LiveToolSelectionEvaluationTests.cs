using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel.ChatCompletion;
using OperationsCopilot.Agent.Plugins;
using OperationsCopilot.Domain.Abstractions;
using OperationsCopilot.Domain.Catalog;
using OperationsCopilot.Domain.Chat;
using OperationsCopilot.TestSupport;
using Shouldly;
using Xunit;

namespace OperationsCopilot.EvaluationTests.Tools;

/// <summary>
/// Measures how well a real model chooses tools, over <see cref="ToolSelectionGoldenSet"/>.
/// </summary>
/// <remarks>
/// <para>
/// Runs against whichever provider the machine has: Azure OpenAI when its endpoint is
/// configured, otherwise a local Ollama server. It skips itself when neither is available, so
/// the rest of the suite stays green on a bare clone.
/// </para>
/// <para>
/// This is the tier to run before changing the system prompt, a tool description, or the model —
/// those are exactly the changes that silently degrade tool selection, and nothing else in the
/// suite can catch them.
/// </para>
/// <para>
/// Thresholds are set for a competent tool-calling model and allow slack, because model output is
/// not deterministic and a suite that fails one run in five teaches people to ignore it. Recall
/// is held to a higher bar than precision: a missed tool means an invented answer, whereas an
/// extra call only costs latency. A small local model will sit closer to the floor than a
/// frontier one — the per-question output below shows where it actually lands.
/// </para>
/// </remarks>
[Collection(DatabaseCollection.Name)]
[Trait("Category", "LiveModel")]
public class LiveToolSelectionEvaluationTests(SeededDatabaseFixture fixture, ITestOutputHelper output)
{
    private const double RequiredRecall = 0.80;
    private const double RequiredPrecision = 0.65;

    [Fact]
    public async Task ToolSelection_MeetsRecallAndPrecisionThresholds()
    {
        var services = await BuildLiveServicesAsync();
        var lines = new List<ResultLine>();

        foreach (var testCase in ToolSelectionGoldenSet.Cases)
        {
            lines.Add(await EvaluateAsync(services, testCase));
        }

        foreach (var line in lines)
        {
            output.WriteLine(
                $"recall={line.Recall:F2} precision={line.Precision:F2}  " +
                $"called=[{string.Join(", ", line.Called)}]  {line.Question}");
        }

        var meanRecall = lines.Average(line => line.Recall);
        var meanPrecision = lines.Average(line => line.Precision);

        output.WriteLine(string.Empty);
        output.WriteLine($"Mean recall     {meanRecall:F3}  (threshold {RequiredRecall:F2})");
        output.WriteLine($"Mean precision  {meanPrecision:F3}  (threshold {RequiredPrecision:F2})");
        output.WriteLine($"Fully correct   {lines.Count(line => line.Recall == 1d)}/{lines.Count}");

        meanRecall.ShouldBeGreaterThanOrEqualTo(RequiredRecall);
        meanPrecision.ShouldBeGreaterThanOrEqualTo(RequiredPrecision);
    }

    [Fact]
    public async Task CombinedQuestions_UseBothADatabaseToolAndTheKnowledgeBase()
    {
        var services = await BuildLiveServicesAsync();

        var combined = ToolSelectionGoldenSet.Cases.Where(c => c.ExpectedTools.Length > 1).ToList();
        var satisfied = 0;

        foreach (var testCase in combined)
        {
            var line = await EvaluateAsync(services, testCase);

            var usedKnowledge = line.Called.Contains(ToolNames.SearchKnowledgeBase);
            var usedDatabase = line.Called.Any(tool => tool != ToolNames.SearchKnowledgeBase);

            if (usedKnowledge && usedDatabase)
            {
                satisfied++;
            }

            output.WriteLine($"{(usedKnowledge && usedDatabase ? "PASS" : "FAIL")}  {testCase.Question}");
        }

        // Answering half a combined question is the failure mode that matters most: the reply
        // reads as authoritative while the rule, or the data, was invented.
        satisfied.ShouldBeGreaterThanOrEqualTo(
            (int)Math.Ceiling(combined.Count * 0.5),
            "Too many combined questions were answered from only one source.");
    }

    [Fact]
    public async Task PolicyQuestion_ProducesCitations()
    {
        var services = await BuildLiveServicesAsync();

        var response = await AskAsync(
            services,
            "What is the restocking fee on opened goods that are returned?");

        output.WriteLine(response.Answer);

        response.ToolCalls.ShouldContain(call => call.FunctionName == ToolNames.SearchKnowledgeBase);
        response.Citations.ShouldNotBeEmpty();
        response.Citations.ShouldContain(citation => citation.SourceFile == "returns-and-warranty-policy.md");
    }

    [Fact]
    public async Task StockQuestion_ReportsFiguresFromTheDatabase()
    {
        var services = await BuildLiveServicesAsync();

        var response = await AskAsync(services, "Which products are running low on stock?");

        output.WriteLine(response.Answer);

        response.ToolCalls.ShouldContain(call => call.FunctionName == ToolNames.GetLowStockProducts);

        // Compared against what the database actually holds rather than a hardcoded SKU, so the
        // check does not depend on whether the model chose to print SKUs or names.
        await using var scope = fixture.CreateScope();
        var lowStock = await scope.ServiceProvider.GetRequiredService<IOperationsRepository>()
            .GetLowStockProductsAsync(new LowStockQuery(), TestContext.Current.CancellationToken);

        var mentioned = lowStock.Count(product =>
            response.Answer.Contains(product.Sku, StringComparison.OrdinalIgnoreCase)
            || response.Answer.Contains(product.Name, StringComparison.OrdinalIgnoreCase));

        output.WriteLine($"Mentioned {mentioned} of {lowStock.Count} low-stock products.");

        // An answer that names most of the real rows can only have come from the tool result.
        mentioned.ShouldBeGreaterThanOrEqualTo(
            (int)Math.Ceiling(lowStock.Count / 2d),
            "The answer did not report the products the tool returned.");
    }

    private async Task<ResultLine> EvaluateAsync(IServiceProvider services, ToolSelectionCase testCase)
    {
        var response = await AskAsync(services, testCase.Question);

        var called = response.ToolCalls
            .Select(call => call.FunctionName)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var recall = testCase.Required.Count == 0
            ? 1d
            : (double)called.Count(testCase.Required.Contains) / testCase.Required.Count;

        var precision = called.Count == 0
            ? 0d
            : (double)called.Count(testCase.Permitted.Contains) / called.Count;

        return new ResultLine(testCase.Question, called, recall, precision);
    }

    private static async Task<ChatResponse> AskAsync(IServiceProvider services, string question)
    {
        await using var scope = services.CreateAsyncScope();

        return await scope.ServiceProvider
            .GetRequiredService<ICopilotAgent>()
            .AskAsync(new ChatRequest(question), TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Swaps the scripted model for a real one, leaving every other part of the wiring — plugins,
    /// database, retrieval, filters — exactly as the application runs it.
    /// </summary>
    private async Task<IServiceProvider> BuildLiveServicesAsync()
    {
        var settings = await LiveModelSettings.ResolveAsync(TestContext.Current.CancellationToken);
        Assert.SkipWhen(settings is null, LiveModelSettings.SkipReason);

        output.WriteLine($"Live model: {settings!.Description}");
        output.WriteLine(string.Empty);

        return fixture.BuildServices(services =>
            services.AddSingleton(settings.CreateChatService()));
    }

    private sealed record ResultLine(
        string Question,
        IReadOnlyList<string> Called,
        double Recall,
        double Precision);
}
