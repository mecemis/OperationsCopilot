using OperationsCopilot.Domain.Planning;
using Shouldly;
using Xunit;

namespace OperationsCopilot.UnitTests.Planning;

/// <summary>
/// The guard that stops a cached plan being replayed for a different subject.
/// </summary>
/// <remarks>
/// The rejection cases here are not hypothetical. Each was measured against nomic-embed-text and
/// scored between 0.77 and 0.91 — inside the band where genuine paraphrases live — so the
/// similarity floor cannot be what separates them. If these tests fail, the cache answers about
/// the wrong product.
/// </remarks>
public class QuestionDiscriminatorsTests
{
    [Theory]
    [InlineData("Tell me about PT-1001.", "Tell me about PT-1006.")]
    [InlineData("How much revenue did we make in the last 30 days?", "How much revenue did we make in the last 90 days?")]
    [InlineData("Who needs to approve a 15% discount?", "Who needs to approve a 25% discount?")]
    [InlineData("What needs reordering in the Rotterdam warehouse?", "What needs reordering in the Chicago warehouse?")]
    [InlineData("Which items in EMEA sold best?", "Which items in APAC sold best?")]
    [InlineData("What is stock like in WH-EU-01?", "What is stock like in WH-AP-01?")]
    public void Match_RejectsQuestionsAboutDifferentSubjects(string question, string other) =>
        QuestionDiscriminators.Match(question, other).ShouldBeFalse();

    [Theory]
    [InlineData("Which products are running low on stock?", "What items need reordering?")]
    [InlineData("What is our returns policy?", "How do returns work here?")]
    [InlineData("How is the reorder threshold calculated?", "How do we work out the reorder threshold?")]
    [InlineData("Tell me about PT-1001.", "Give me the details for PT-1001.")]
    [InlineData("What sold best in EMEA?", "Which products performed best in EMEA?")]
    public void Match_AcceptsQuestionsAboutTheSameSubject(string question, string other) =>
        QuestionDiscriminators.Match(question, other).ShouldBeTrue();

    [Fact]
    public void Extract_TreatsAnythingCarryingADigitAsASubject()
    {
        var discriminators = QuestionDiscriminators.Extract(
            "Show me PT-1001 stock in WH-EU-01 over 30 days at a 15% discount");

        discriminators.ShouldBe(
            ["PT-1001", "WH-EU-01", "30", "15%"],
            ignoreOrder: true);
    }

    [Fact]
    public void Extract_IgnoresCapitalisationAtTheStartOfASentence()
    {
        // "Which" and "Tell" are capitalised only because a sentence starts there. Counting them
        // would make every question discriminate on its own opening word and nothing would ever
        // match anything.
        QuestionDiscriminators.Extract("Which products are low? Tell me now.").ShouldBeEmpty();
    }

    [Fact]
    public void Extract_TreatsAProperNounMidSentenceAsASubject() =>
        QuestionDiscriminators.Extract("What is stock like in Rotterdam?")
            .ShouldBe(["Rotterdam"]);

    [Fact]
    public void Extract_IsCaseInsensitive() =>
        QuestionDiscriminators.Match("Tell me about pt-1001.", "What about PT-1001?").ShouldBeTrue();

    [Fact]
    public void Extract_HandlesAnEmptyQuestion() =>
        QuestionDiscriminators.Extract("   ").ShouldBeEmpty();
}
