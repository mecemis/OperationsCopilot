using OperationsCopilot.Domain.Abstractions;
using OperationsCopilot.Domain.Planning;
using OperationsCopilot.Infrastructure.Embeddings;
using OperationsCopilot.TestSupport;
using Shouldly;
using Xunit;

namespace OperationsCopilot.EvaluationTests.Planning;

/// <summary>
/// Characterises the similarity scores plan reuse actually has to work with, using a real
/// embedding model.
/// </summary>
/// <remarks>
/// <para>
/// This is the test that decides whether the feature is safe, and it is the one to re-run after
/// changing the embedding model — the numbers below describe one model, not embeddings in
/// general. The offline tier cannot stand in for it: the deterministic provider matches
/// vocabulary rather than meaning, so it says nothing about how a real model scores a paraphrase.
/// </para>
/// <para>
/// Against nomic-embed-text the bands overlap, which is the finding that shaped the design.
/// Paraphrases run from about 0.54 to 0.94 and same-shape different-rule questions from about
/// 0.42 to 0.78, so no floor admits every paraphrase and rejects every near miss. The floor is
/// therefore set above the near misses and the hit rate takes the loss.
/// </para>
/// </remarks>
[Trait("Category", "LiveModel")]
public class PlanCacheSeparationTests(ITestOutputHelper output)
{
    /// <summary>Mirrors the shipped default in <c>appsettings.json</c>.</summary>
    private const double MinimumSimilarity = 0.85;

    [Fact]
    public async Task NearMissQuestions_AllScoreBelowTheConfiguredFloor()
    {
        var scores = await ScoreAsync("NEAR MISS", PlanReuseGoldenSet.NearMisses);

        // A near miss admitted by the floor is a plan replayed for a different rule: the tools run
        // and return real data, for a question nobody asked.
        scores.Max().ShouldBeLessThan(
            MinimumSimilarity,
            "A question about a different rule scored above SemanticCache:MinimumSimilarity.");
    }

    [Fact]
    public async Task ParaphrasedQuestions_ClearTheFloorOftenEnoughToBeWorthCaching()
    {
        var paraphrase = await ScoreAsync("PARAPHRASE", PlanReuseGoldenSet.Paraphrases);
        var nearMiss = await ScoreAsync("NEAR MISS", PlanReuseGoldenSet.NearMisses);

        var hits = paraphrase.Count(score => score >= MinimumSimilarity);

        output.WriteLine(string.Empty);
        output.WriteLine($"Paraphrases clearing {MinimumSimilarity:F2}: {hits}/{paraphrase.Count}");
        output.WriteLine($"Mean paraphrase {paraphrase.Average():F3}  mean near miss {nearMiss.Average():F3}");

        // Two claims, both weaker than "every paraphrase hits" — which is measurably false and
        // the reason the floor is set where it is.
        paraphrase.Max().ShouldBeGreaterThanOrEqualTo(
            MinimumSimilarity,
            "No paraphrase clears the floor, so the cache can never hit.");

        paraphrase.Average().ShouldBeGreaterThan(
            nearMiss.Average(),
            "Paraphrases no longer score higher than near misses; the floor is measuring noise.");
    }

    [Fact]
    public async Task ConfusableQuestions_AreRejectedByTheDiscriminatorGuard()
    {
        var scores = await ScoreAsync("CONFUSABLE", PlanReuseGoldenSet.Confusable);

        foreach (var pair in PlanReuseGoldenSet.Confusable)
        {
            QuestionDiscriminators.Match(pair.Question, pair.Other).ShouldBeFalse(
                $"'{pair.Question}' and '{pair.Other}' are about different subjects.");
        }

        // The point of the guard, stated as an assertion: some of these sit above the floor, so
        // deleting the guard as redundant would immediately start answering the wrong question.
        scores.Count(score => score >= MinimumSimilarity).ShouldBeGreaterThan(
            0,
            "No confusable pair cleared the floor, so this run does not prove the guard is needed.");
    }

    private async Task<List<double>> ScoreAsync(string label, QuestionPair[] pairs)
    {
        var embeddings = await BuildEmbeddingServiceAsync();
        var scores = new List<double>(pairs.Length);

        output.WriteLine($"== {label}");

        foreach (var pair in pairs)
        {
            var score = CosineSimilarity(
                await embeddings.EmbedAsync(pair.Question, TestContext.Current.CancellationToken),
                await embeddings.EmbedAsync(pair.Other, TestContext.Current.CancellationToken));

            scores.Add(score);
            output.WriteLine($"  {score:F4}  {pair.Question}  |  {pair.Other}");
        }

        output.WriteLine($"  -> min {scores.Min():F4}  mean {scores.Average():F4}  max {scores.Max():F4}");
        output.WriteLine(string.Empty);

        return scores;
    }

    private async Task<IEmbeddingService> BuildEmbeddingServiceAsync()
    {
        var settings = await LiveModelSettings.ResolveAsync(TestContext.Current.CancellationToken);
        Assert.SkipWhen(settings is null, LiveModelSettings.SkipReason);

        output.WriteLine($"Live embeddings: {settings!.Provider}");
        output.WriteLine(string.Empty);

        return new GeneratedEmbeddingService(
            settings.Factory.CreateEmbeddingGenerator(),
            settings.Factory.EmbeddingDimensions);
    }

    /// <summary>
    /// The same measure pgvector's <c>&lt;=&gt;</c> reports as <c>1 - distance</c>, computed here
    /// so the test needs no database.
    /// </summary>
    private static double CosineSimilarity(float[] left, float[] right)
    {
        double dot = 0, leftMagnitude = 0, rightMagnitude = 0;

        for (var i = 0; i < left.Length; i++)
        {
            dot += left[i] * right[i];
            leftMagnitude += left[i] * left[i];
            rightMagnitude += right[i] * right[i];
        }

        return dot / (Math.Sqrt(leftMagnitude) * Math.Sqrt(rightMagnitude));
    }
}
