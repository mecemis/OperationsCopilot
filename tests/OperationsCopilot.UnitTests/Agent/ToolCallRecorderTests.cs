using OperationsCopilot.Agent;
using OperationsCopilot.Domain.Chat;
using OperationsCopilot.Domain.Knowledge;
using Shouldly;
using Xunit;

namespace OperationsCopilot.UnitTests.Agent;

public class ToolCallRecorderTests
{
    private readonly ToolCallRecorder _recorder = new();

    [Fact]
    public void RecordToolCall_PreservesInvocationOrder()
    {
        _recorder.RecordToolCall(Invocation("GetLowStockProducts"));
        _recorder.RecordToolCall(Invocation("SearchKnowledgeBase"));

        _recorder.ToolCalls.Select(c => c.FunctionName)
            .ShouldBe(["GetLowStockProducts", "SearchKnowledgeBase"]);
    }

    [Fact]
    public void ToolCalls_ExposesASnapshotRatherThanTheLiveList()
    {
        _recorder.RecordToolCall(Invocation("GetSalesSummary"));
        var snapshot = _recorder.ToolCalls;

        _recorder.RecordToolCall(Invocation("GetProductDetails"));

        snapshot.Count.ShouldBe(1);
        _recorder.ToolCalls.Count.ShouldBe(2);
    }

    [Fact]
    public void RecordRetrieval_KeepsTheFirstOccurrenceOfARepeatedChunk()
    {
        var chunkId = Guid.CreateVersion7();

        _recorder.RecordRetrieval([Passage(chunkId, "inventory-policy.md", 0.91)]);
        _recorder.RecordRetrieval([Passage(chunkId, "inventory-policy.md", 0.55)]);

        // Citation numbers are handed to the model as soon as a passage is retrieved, so a
        // second search must not renumber or duplicate what is already cited.
        _recorder.RetrievedPassages.Count.ShouldBe(1);
        _recorder.RetrievedPassages[0].Score.ShouldBe(0.91);
    }

    [Fact]
    public void RecordRetrieval_AppendsNewChunksAfterExistingOnes()
    {
        var first = Passage(Guid.CreateVersion7(), "inventory-policy.md", 0.9);
        var second = Passage(Guid.CreateVersion7(), "pricing-and-discount-policy.md", 0.8);

        _recorder.RecordRetrieval([first]);
        _recorder.RecordRetrieval([first, second]);

        _recorder.RetrievedPassages.Select(p => p.SourceFile)
            .ShouldBe(["inventory-policy.md", "pricing-and-discount-policy.md"]);
    }

    private static ToolInvocation Invocation(string function)
        => new("Operations", function, new Dictionary<string, string?>(), 5, Succeeded: true);

    private static KnowledgeSearchResult Passage(Guid id, string sourceFile, double score)
        => new(id, sourceFile, "Policy", "Section", 0, "content", score);
}
