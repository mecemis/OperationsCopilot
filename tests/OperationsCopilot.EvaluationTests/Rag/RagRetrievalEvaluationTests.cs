using Microsoft.Extensions.DependencyInjection;
using OperationsCopilot.Domain.Abstractions;
using OperationsCopilot.Domain.Knowledge;
using OperationsCopilot.TestSupport;
using Shouldly;
using Xunit;

namespace OperationsCopilot.EvaluationTests.Rag;

/// <summary>
/// Measures retrieval quality over <see cref="RetrievalGoldenSet"/> against a real pgvector
/// index.
/// </summary>
/// <remarks>
/// <para>
/// These run with the deterministic embedding provider, so they are free, offline and identical
/// on every machine. That provider matches on shared vocabulary rather than on meaning, so the
/// thresholds below are a floor for <em>lexical</em> retrieval, and a real embedding model should
/// comfortably beat them. Treat a failure as "chunking, indexing or search broke", not as
/// "the model got worse".
/// </para>
/// <para>
/// The thresholds are set below observed performance with headroom, so ordinary run-to-run
/// variation does not fail the build while a genuine regression does.
/// </para>
/// </remarks>
[Collection(DatabaseCollection.Name)]
public class RagRetrievalEvaluationTests(SeededDatabaseFixture fixture, ITestOutputHelper output)
{
    private const int TopK = 5;

    /// <summary>Threshold used for evaluation. Deliberately 0 so the metrics see the full ranking.</summary>
    private const double NoScoreFloor = 0d;

    [Fact]
    public void KnowledgeBase_IsFullyIndexed()
    {
        fixture.IndexingResult.DocumentsProcessed.ShouldBe(5);
        fixture.IndexingResult.TotalChunks.ShouldBeGreaterThan(20);
    }

    [Fact]
    public async Task Retrieval_MeetsTheRecallAndMrrThresholds()
    {
        var report = await EvaluateAsync();

        foreach (var line in report.Lines)
        {
            output.WriteLine(
                $"{(line.Hit ? "PASS" : "FAIL")}  rr={line.ReciprocalRank:F2}  " +
                $"recall@{TopK}={line.Recall:F2}  top={line.TopSource,-34}  {line.Question}");
        }

        output.WriteLine(string.Empty);
        output.WriteLine($"MRR                 {report.MeanReciprocalRank:F3}");
        output.WriteLine($"Recall@{TopK}            {report.MeanRecall:F3}");
        output.WriteLine($"Top-1 accuracy      {report.TopOneAccuracy:F3}");
        output.WriteLine($"Cases               {report.Lines.Count}");

        report.MeanReciprocalRank.ShouldBeGreaterThanOrEqualTo(0.80);
        report.MeanRecall.ShouldBeGreaterThanOrEqualTo(0.80);
        report.TopOneAccuracy.ShouldBeGreaterThanOrEqualTo(0.70);
    }

    [Fact]
    public async Task Retrieval_FindsTheRightDocumentForEveryGoldenCase()
    {
        var report = await EvaluateAsync();

        var misses = report.Lines.Where(line => !line.Hit).Select(line => line.Question).ToList();

        // Every case in the golden set should retrieve its document somewhere in the top K.
        // A miss here means a question a real user would ask returns nothing useful.
        misses.ShouldBeEmpty($"Golden cases with no relevant document in the top {TopK}.");
    }

    [Fact]
    public async Task Retrieval_ReturnsTheExpectedPassageNotJustTheExpectedFile()
    {
        await using var scope = fixture.CreateScope();
        var search = scope.ServiceProvider.GetRequiredService<IKnowledgeBaseSearch>();

        var failures = new List<string>();

        foreach (var testCase in RetrievalGoldenSet.Cases.Where(c => c.MustContain is not null))
        {
            var results = await search.SearchAsync(
                testCase.Question,
                TopK,
                NoScoreFloor,
                TestContext.Current.CancellationToken);

            var found = results.Any(r => r.Content.Contains(testCase.MustContain!, StringComparison.OrdinalIgnoreCase));

            if (!found)
            {
                failures.Add($"'{testCase.Question}' did not retrieve a passage containing '{testCase.MustContain}'.");
            }
        }

        foreach (var failure in failures)
        {
            output.WriteLine(failure);
        }

        // Retrieving the right file but the wrong section still gives the model nothing to
        // answer with, so this is checked separately from the file-level metrics.
        failures.Count.ShouldBeLessThanOrEqualTo(2, "Too many golden cases retrieved the wrong section.");
    }

    [Fact]
    public async Task Search_RanksResultsByDescendingScore()
    {
        await using var scope = fixture.CreateScope();
        var search = scope.ServiceProvider.GetRequiredService<IKnowledgeBaseSearch>();

        var results = await search.SearchAsync(
            "supplier lead times and expediting",
            TopK,
            NoScoreFloor,
            TestContext.Current.CancellationToken);

        results.Select(r => r.Score).ShouldBeInOrder(SortDirection.Descending);
    }

    [Fact]
    public async Task Search_KeepsScoresWithinTheDocumentedRange()
    {
        await using var scope = fixture.CreateScope();
        var search = scope.ServiceProvider.GetRequiredService<IKnowledgeBaseSearch>();

        var results = await search.SearchAsync(
            "warranty",
            TopK,
            NoScoreFloor,
            TestContext.Current.CancellationToken);

        // The IKnowledgeBaseSearch contract promises cosine similarity in [0, 1]; callers
        // (and the minimum-score filter) depend on that.
        results.ShouldAllBe(r => r.Score >= 0d && r.Score <= 1d);
    }

    [Fact]
    public async Task Search_AppliesTheMinimumScoreFilter()
    {
        await using var scope = fixture.CreateScope();
        var search = scope.ServiceProvider.GetRequiredService<IKnowledgeBaseSearch>();

        var unfiltered = await search.SearchAsync(
            "restocking fee",
            TopK,
            NoScoreFloor,
            TestContext.Current.CancellationToken);

        var filtered = await search.SearchAsync(
            "restocking fee",
            TopK,
            0.99,
            TestContext.Current.CancellationToken);

        unfiltered.ShouldNotBeEmpty();
        filtered.Count.ShouldBeLessThan(unfiltered.Count);
    }

    [Fact]
    public async Task Search_ReturnsNothingForAnEmptyQuery()
    {
        await using var scope = fixture.CreateScope();
        var search = scope.ServiceProvider.GetRequiredService<IKnowledgeBaseSearch>();

        var results = await search.SearchAsync("   ", TopK, NoScoreFloor, TestContext.Current.CancellationToken);

        results.ShouldBeEmpty();
    }

    private async Task<EvaluationReport> EvaluateAsync()
    {
        await using var scope = fixture.CreateScope();
        var search = scope.ServiceProvider.GetRequiredService<IKnowledgeBaseSearch>();

        var lines = new List<EvaluationLine>();

        foreach (var testCase in RetrievalGoldenSet.Cases)
        {
            var results = await search.SearchAsync(
                testCase.Question,
                TopK,
                NoScoreFloor,
                TestContext.Current.CancellationToken);

            lines.Add(Score(testCase, results));
        }

        return new EvaluationReport(lines);
    }

    private static EvaluationLine Score(RetrievalCase testCase, IReadOnlyList<KnowledgeSearchResult> results)
    {
        var ranked = results.Select(r => r.SourceFile).ToList();

        return new EvaluationLine(
            testCase.Question,
            ranked.FirstOrDefault() ?? "(none)",
            RetrievalMetrics.ReciprocalRank(ranked, testCase.Relevant),
            RetrievalMetrics.RecallAtK(ranked, testCase.Relevant, TopK),
            ranked.Take(TopK).Any(testCase.Relevant.Contains));
    }

    private sealed record EvaluationLine(
        string Question,
        string TopSource,
        double ReciprocalRank,
        double Recall,
        bool Hit);

    private sealed record EvaluationReport(IReadOnlyList<EvaluationLine> Lines)
    {
        public double MeanReciprocalRank => Lines.Average(l => l.ReciprocalRank);

        public double MeanRecall => Lines.Average(l => l.Recall);

        public double TopOneAccuracy => Lines.Count(l => l.ReciprocalRank == 1d) / (double)Lines.Count;
    }
}
