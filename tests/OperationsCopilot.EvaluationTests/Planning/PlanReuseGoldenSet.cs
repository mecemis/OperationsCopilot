namespace OperationsCopilot.EvaluationTests.Planning;

/// <summary>Two questions and what the cache is supposed to conclude about them.</summary>
public sealed record QuestionPair(string Question, string Other);

/// <summary>
/// The three ways two questions can relate, for measuring whether plan reuse is safe.
/// </summary>
/// <remarks>
/// Split into three groups rather than the usual two because the third group is what makes this
/// feature hard. <see cref="Confusable"/> pairs are the same question about a different subject:
/// they are near-identical by every measure an embedding model has, and reusing a plan across one
/// produces an answer that is fluent, sourced from live data, and about the wrong thing.
/// </remarks>
public static class PlanReuseGoldenSet
{
    /// <summary>Different wording, same request. These are what the cache exists to catch.</summary>
    public static readonly QuestionPair[] Paraphrases =
    [
        new("Which products are running low on stock?", "What items need reordering?"),
        new("Which products are running low on stock?", "Anything below its reorder threshold right now?"),
        new("Which products are running low on stock?", "Which items should we raise a purchase order for?"),
        new("What is our returns policy?", "How do returns work here?"),
        new("What is our returns policy?", "Tell me the rules for returning goods."),
        new("How is the reorder threshold calculated?", "How do we work out the reorder threshold?"),
        new("What is the warranty period for safety equipment?", "How long is safety equipment under warranty?"),
        new("What were our best selling categories last quarter?", "Which categories sold best last quarter?"),
        new("Who needs to approve a discount?", "Who signs off on a discount?"),
    ];

    /// <summary>
    /// Same shape, different rule. Nothing in the wording marks them apart, so the similarity
    /// floor is the only thing that can reject these.
    /// </summary>
    public static readonly QuestionPair[] NearMisses =
    [
        new("What is our returns policy?", "What is the warranty period for safety equipment?"),
        new("How is the reorder threshold calculated?", "How is safety stock calculated?"),
        new("Which products are running low on stock?", "Which products sell the best?"),
        new("Who needs to approve a discount?", "Who needs to approve a purchase order?"),
        new("What is our returns policy?", "What is our pricing policy?"),
        new("What is our returns policy?", "What is our supplier onboarding policy?"),
        new("Which products are running low on stock?", "Which products are discontinued?"),
        new("How long is the warranty on power tools?", "How long is the lead time on power tools?"),
    ];

    /// <summary>
    /// Same question, different subject. The floor cannot reject these — they score as high as
    /// genuine paraphrases — so the discriminator guard has to.
    /// </summary>
    public static readonly QuestionPair[] Confusable =
    [
        new("Tell me about PT-1001.", "Tell me about PT-1006."),
        new("How much revenue did we make in the last 30 days?", "How much revenue did we make in the last 90 days?"),
        new("Who needs to approve a 15% discount?", "Who needs to approve a 25% discount?"),
        new("What needs reordering in the Rotterdam warehouse?", "What needs reordering in the Chicago warehouse?"),
        new("Which items in EMEA sold best?", "Which items in APAC sold best?"),
    ];
}
