namespace OperationsCopilot.EvaluationTests.Rag;

/// <summary>
/// Standard information-retrieval metrics, computed over the ranked list of source documents a
/// search returned.
/// </summary>
/// <remarks>
/// Retrieval quality is a number, not a vibe. Measuring it means a change to chunking, to the
/// embedding model, or to the score threshold shows up as a moved metric rather than as an
/// answer that "feels worse".
/// </remarks>
public static class RetrievalMetrics
{
    /// <summary>
    /// Fraction of the relevant documents that appear in the top <paramref name="k"/> results.
    /// 1.0 means every document that should have been found was.
    /// </summary>
    public static double RecallAtK(
        IReadOnlyList<string> rankedSources,
        IReadOnlySet<string> relevant,
        int k)
    {
        if (relevant.Count == 0)
        {
            return 1d;
        }

        var found = rankedSources.Take(k).Distinct(StringComparer.OrdinalIgnoreCase)
            .Count(relevant.Contains);

        return (double)found / relevant.Count;
    }

    /// <summary>
    /// Reciprocal of the rank of the first relevant result: 1.0 when the top hit is right, 0.5
    /// when it is second, 0 when nothing relevant was retrieved. Averaged over a query set this
    /// is MRR, and it is the metric that best tracks "did the right passage reach the model".
    /// </summary>
    public static double ReciprocalRank(IReadOnlyList<string> rankedSources, IReadOnlySet<string> relevant)
    {
        for (var i = 0; i < rankedSources.Count; i++)
        {
            if (relevant.Contains(rankedSources[i]))
            {
                return 1d / (i + 1);
            }
        }

        return 0d;
    }

    /// <summary>Fraction of the top <paramref name="k"/> results that are relevant.</summary>
    public static double PrecisionAtK(
        IReadOnlyList<string> rankedSources,
        IReadOnlySet<string> relevant,
        int k)
    {
        var top = rankedSources.Take(k).ToList();

        return top.Count == 0 ? 0d : (double)top.Count(relevant.Contains) / top.Count;
    }
}
