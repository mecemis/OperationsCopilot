using System.ComponentModel.DataAnnotations;

namespace OperationsCopilot.Infrastructure.Options;

/// <summary>
/// Semantic plan cache settings, bound from the <c>SemanticCache</c> configuration section.
/// </summary>
public sealed class SemanticCacheOptions
{
    public const string SectionName = "SemanticCache";

    /// <summary>Turns plan reuse off entirely. Every turn then plans from the model, as before.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Minimum cosine similarity between the incoming question and a stored one before its plan
    /// may be reused.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Measured, and the measurement is uncomfortable: against nomic-embed-text the band for
    /// genuine paraphrases (0.54–0.94) overlaps the band for questions of the same shape about a
    /// different rule (0.42–0.78). There is no floor that admits every paraphrase and rejects
    /// every near miss, so this one is set above the highest measured near miss — "who approves a
    /// discount" against "who approves a purchase order", at 0.775 — and buys precision by giving
    /// up hit rate. Loose paraphrases simply miss and cost one model round trip, which is the
    /// cheap direction to be wrong in.
    /// </para>
    /// <para>
    /// The floor is not the only guard, and cannot be: see
    /// <see cref="Domain.Planning.QuestionDiscriminators"/> for the subjects problem, which no
    /// value here solves. Re-measure both with <c>PlanCacheSeparationTests</c> after changing the
    /// embedding model; these numbers describe one.
    /// </para>
    /// </remarks>
    [Range(0d, 1d)]
    public double MinimumSimilarity { get; set; } = 0.85;

    /// <summary>
    /// Nearest stored questions to consider. The closest is not always the usable one — a nearer
    /// entry may be rejected for naming a different product — so lookup examines a few.
    /// </summary>
    [Range(1, 20)]
    public int Candidates { get; set; } = 5;

    /// <summary>
    /// How long a plan stays reusable. A backstop rather than the main safety property: plans are
    /// re-executed against live data, so this bounds drift in what a good plan looks like — a
    /// renamed tool, a new filter, a reworded document — not staleness of the figures.
    /// </summary>
    [Range(1, 43200)]
    public int TimeToLiveMinutes { get; set; } = 1440;

    /// <summary>Entries kept. Past this, the least-reused and oldest are dropped on the next write.</summary>
    [Range(10, 100000)]
    public int MaxEntries { get; set; } = 500;

    public TimeSpan TimeToLive => TimeSpan.FromMinutes(TimeToLiveMinutes);
}
