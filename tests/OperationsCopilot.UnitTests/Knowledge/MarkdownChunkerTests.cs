using OperationsCopilot.Infrastructure.Knowledge;
using Shouldly;
using Xunit;

namespace OperationsCopilot.UnitTests.Knowledge;

public class MarkdownChunkerTests
{
    private const string Document =
        """
        # Returns Policy

        Owner: Customer Operations.

        ## Return Window

        Customers may return unopened goods within 30 days.

        ## Restocking Fees

        Opened goods carry a 15% restocking fee.
        """;

    [Fact]
    public void Chunk_UsesTopLevelHeadingAsDocumentTitle()
    {
        var chunks = new MarkdownChunker().Chunk(Document);

        chunks.ShouldAllBe(chunk => chunk.DocumentTitle == "Returns Policy");
    }

    [Fact]
    public void Chunk_SplitsOnSectionHeadings()
    {
        var chunks = new MarkdownChunker().Chunk(Document);

        chunks.Select(c => c.Heading).ShouldBe(["Returns Policy", "Return Window", "Restocking Fees"]);
    }

    [Fact]
    public void Chunk_NumbersChunksSequentiallyWithinTheDocument()
    {
        var chunks = new MarkdownChunker().Chunk(Document);

        chunks.Select(c => c.ChunkIndex).ShouldBe([0, 1, 2]);
    }

    [Fact]
    public void Chunk_PrefixesEmbeddingInputWithTitleAndHeading()
    {
        var chunks = new MarkdownChunker().Chunk(Document);

        var restocking = chunks.Single(c => c.Heading == "Restocking Fees");

        // Without this prefix the chunk reads as a bare "15%" with nothing saying what of.
        restocking.EmbeddingInput.ShouldStartWith("Returns Policy — Restocking Fees");
        restocking.EmbeddingInput.ShouldContain("15% restocking fee");
    }

    [Fact]
    public void Chunk_DoesNotRepeatTheTitleWhenItIsAlsoTheHeading()
    {
        var chunks = new MarkdownChunker().Chunk(Document);

        var preamble = chunks.First();

        preamble.EmbeddingInput.ShouldStartWith("Returns Policy\n\n");
        preamble.EmbeddingInput.ShouldNotContain("Returns Policy — Returns Policy");
    }

    [Fact]
    public void Chunk_SplitsLongSectionsAndOverlapsThem()
    {
        var paragraph = string.Join(" ", Enumerable.Repeat("Stock is counted quarterly.", 12));
        var longDocument = $"# Inventory\n\n## Counting\n\n{paragraph}\n\n{paragraph}\n\n{paragraph}";

        var chunks = new MarkdownChunker(maxChunkCharacters: 400, overlapCharacters: 80)
            .Chunk(longDocument);

        chunks.Count.ShouldBeGreaterThan(1);

        // Overlap means the tail of one chunk reappears at the head of the next, so a rule that
        // straddles the boundary is retrievable from either side.
        var tail = chunks[0].Content[^40..];
        chunks[1].Content.ShouldContain(tail[..20]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\n\n")]
    public void Chunk_ReturnsNothingForEmptyInput(string markdown)
        => new MarkdownChunker().Chunk(markdown).ShouldBeEmpty();

    [Fact]
    public void Chunk_IgnoresHashesThatAreNotHeadings()
    {
        var chunks = new MarkdownChunker().Chunk("# Doc\n\nUse #4 grit, not ###invalid.");

        chunks.Count.ShouldBe(1);
        chunks[0].Content.ShouldContain("#4 grit");
    }
}
