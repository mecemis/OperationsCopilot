using Microsoft.Extensions.DependencyInjection;
using OperationsCopilot.Domain.Abstractions;
using OperationsCopilot.TestSupport;
using Shouldly;
using Xunit;

namespace OperationsCopilot.EvaluationTests.Rag;

/// <summary>
/// Characterises the similarity scores retrieval actually produces, and checks the configured
/// minimum-score floor against them.
/// </summary>
/// <remarks>
/// The floor in <c>Rag:MinimumScore</c> is the one setting most likely to be picked out of the
/// air and then quietly break retrieval: set it too high and every search returns nothing, which
/// looks to the user like a knowledge base with no content in it. These tests pin it to measured
/// behaviour, and print the distribution so the number can be re-chosen from evidence when the
/// embedding model changes.
/// </remarks>
[Collection(DatabaseCollection.Name)]
public class ScoreDistributionTests(SeededDatabaseFixture fixture, ITestOutputHelper output)
{
    /// <summary>Questions with no answer anywhere in the knowledge base.</summary>
    private static readonly string[] OffTopicQueries =
    [
        "What is the capital of France?",
        "Write me a poem about the sea.",
        "How do I reset my email password?",
    ];

    [Fact]
    public async Task RelevantQueries_ScoreAboveTheConfiguredFloor()
    {
        await using var scope = fixture.CreateScope();
        var search = scope.ServiceProvider.GetRequiredService<IKnowledgeBaseSearch>();

        var topScores = new List<double>();

        foreach (var testCase in RetrievalGoldenSet.Cases)
        {
            var results = await search.SearchAsync(
                testCase.Question,
                1,
                minimumScore: 0d,
                TestContext.Current.CancellationToken);

            var score = results.Count > 0 ? results[0].Score : 0d;
            topScores.Add(score);

            output.WriteLine($"{score:F3}  {testCase.Question}");
        }

        output.WriteLine(string.Empty);
        output.WriteLine($"min {topScores.Min():F3}  median {Median(topScores):F3}  max {topScores.Max():F3}");

        // Every golden question must clear the floor, or the agent is told "no relevant passage"
        // for a question the knowledge base plainly answers.
        topScores.Min().ShouldBeGreaterThan(
            KnowledgeBaseDefaults.MinimumScore,
            "A golden question scored below Rag:MinimumScore and would return no passages.");
    }

    [Fact]
    public async Task OffTopicQueries_ScoreBelowRelevantOnes()
    {
        await using var scope = fixture.CreateScope();
        var search = scope.ServiceProvider.GetRequiredService<IKnowledgeBaseSearch>();

        var offTopic = new List<double>();

        foreach (var query in OffTopicQueries)
        {
            var results = await search.SearchAsync(query, 1, 0d, TestContext.Current.CancellationToken);
            var score = results.Count > 0 ? results[0].Score : 0d;

            offTopic.Add(score);
            output.WriteLine($"{score:F3}  {query}");
        }

        var relevant = await search.SearchAsync(
            "What is the restocking fee on returned goods?",
            1,
            0d,
            TestContext.Current.CancellationToken);

        // Separation between on-topic and off-topic is what makes a score floor meaningful at all.
        offTopic.Max().ShouldBeLessThan(relevant[0].Score);
    }

    private static double Median(List<double> values)
    {
        var sorted = values.Order().ToList();
        return sorted.Count % 2 == 1
            ? sorted[sorted.Count / 2]
            : (sorted[(sorted.Count / 2) - 1] + sorted[sorted.Count / 2]) / 2d;
    }
}

/// <summary>Mirrors the shipped defaults in <c>appsettings.json</c> so the tests assert the real values.</summary>
internal static class KnowledgeBaseDefaults
{
    public const double MinimumScore = 0.15;
}
