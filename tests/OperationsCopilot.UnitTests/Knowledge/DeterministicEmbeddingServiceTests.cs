using OperationsCopilot.Infrastructure.Embeddings;
using OperationsCopilot.Infrastructure.Ai;
using Shouldly;
using Xunit;

namespace OperationsCopilot.UnitTests.Knowledge;

public class DeterministicEmbeddingServiceTests
{
    private readonly DeterministicEmbeddingService _service = new();

    [Fact]
    public async Task EmbedAsync_ProducesVectorsOfTheProvidersDeclaredWidth()
    {
        var vector = await _service.EmbedAsync("reorder threshold", TestContext.Current.CancellationToken);

        vector.Length.ShouldBe(AiClientFactory.DeterministicEmbeddingDimensions);
    }

    [Fact]
    public async Task EmbedAsync_IsStableAcrossCalls()
    {
        var first = await _service.EmbedAsync("supplier lead time", TestContext.Current.CancellationToken);
        var second = await _service.EmbedAsync("supplier lead time", TestContext.Current.CancellationToken);

        second.ShouldBe(first);
    }

    [Fact]
    public async Task EmbedAsync_ProducesUnitLengthVectors()
    {
        var vector = await _service.EmbedAsync(
            "warranty period for power tools",
            TestContext.Current.CancellationToken);

        // Unit length is what keeps cosine similarity inside [0, 1], which the
        // IKnowledgeBaseSearch contract promises to callers.
        Magnitude(vector).ShouldBe(1f, tolerance: 0.0001f);
    }

    [Fact]
    public async Task EmbedAsync_ScoresRelatedTextHigherThanUnrelatedText()
    {
        var query = await _service.EmbedAsync(
            "How long is the warranty on power tools?",
            TestContext.Current.CancellationToken);

        var related = await _service.EmbedAsync(
            "Power Tools carry a 24 month warranty against defects.",
            TestContext.Current.CancellationToken);

        var unrelated = await _service.EmbedAsync(
            "Warehouse WH-AP-01 in Singapore serves the APAC region.",
            TestContext.Current.CancellationToken);

        CosineSimilarity(query, related).ShouldBeGreaterThan(CosineSimilarity(query, unrelated));
    }

    [Fact]
    public async Task EmbedAsync_TreatsSingularAndPluralAsTheSameTerm()
    {
        var singular = await _service.EmbedAsync("discontinued product", TestContext.Current.CancellationToken);
        var plural = await _service.EmbedAsync("discontinued products", TestContext.Current.CancellationToken);

        CosineSimilarity(singular, plural).ShouldBe(1f, tolerance: 0.0001f);
    }

    [Fact]
    public async Task EmbedAsync_ReturnsAZeroVectorWhenNothingSurvivesTokenizing()
    {
        var vector = await _service.EmbedAsync("!!! ???", TestContext.Current.CancellationToken);

        // Must not divide by zero, and must not produce NaN, which would poison the index.
        vector.ShouldAllBe(component => component == 0f);
    }

    [Fact]
    public async Task EmbedBatchAsync_ReturnsResultsAlignedWithTheInput()
    {
        string[] texts = ["cutting disc", "safety goggles", "impact driver"];

        var batch = await _service.EmbedBatchAsync(texts, TestContext.Current.CancellationToken);

        batch.Count.ShouldBe(3);
        for (var i = 0; i < texts.Length; i++)
        {
            batch[i].ShouldBe(await _service.EmbedAsync(texts[i], TestContext.Current.CancellationToken));
        }
    }

    private static float Magnitude(float[] vector) => MathF.Sqrt(vector.Sum(v => v * v));

    private static float CosineSimilarity(float[] left, float[] right)
        => left.Zip(right, (a, b) => a * b).Sum();
}
