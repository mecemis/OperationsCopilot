using System.Text;
using OperationsCopilot.Domain.Abstractions;
using OperationsCopilot.Infrastructure.Ai;

namespace OperationsCopilot.Infrastructure.Embeddings;

/// <summary>
/// A local, offline embedding service: hashed bag-of-words over unigrams and bigrams,
/// term-frequency weighted and L2-normalized.
/// </summary>
/// <remarks>
/// <para>
/// This exists so the whole application — including the RAG evaluation suite in CI — can run
/// with no Azure subscription and no per-run cost. Because the vectors are non-negative and unit
/// length, cosine similarity lands in [0, 1] and behaves like weighted lexical overlap.
/// </para>
/// <para>
/// It matches on shared wording, not on meaning: a query that paraphrases a document without
/// reusing its vocabulary will not retrieve it. That is fine for testing retrieval plumbing and
/// for keyword-style questions, and it is not a substitute for a real embedding model.
/// Set <c>Ai:EmbeddingProvider</c> to <c>AzureOpenAI</c> or <c>Ollama</c> for anything beyond
/// that.
/// </para>
/// </remarks>
public sealed class DeterministicEmbeddingService : IEmbeddingService
{
    /// <summary>Bigrams carry less weight than unigrams but let exact phrases outrank loose word overlap.</summary>
    private const float BigramWeight = 0.5f;

    private const int MinimumTokenLength = 2;

    /// <summary>
    /// Very common words carry no retrieval signal and would otherwise dominate short queries.
    /// Deliberately small: an aggressive list starts discarding real terms.
    /// </summary>
    private static readonly HashSet<string> StopWords = new(StringComparer.Ordinal)
    {
        "a", "an", "and", "are", "as", "at", "be", "but", "by", "do", "for", "from", "has", "have",
        "how", "i", "if", "in", "into", "is", "it", "its", "me", "my", "of", "on", "or", "our",
        "that", "the", "their", "them", "there", "these", "they", "this", "to", "was", "we",
        "were", "what", "when", "which", "who", "why", "will", "with", "you", "your",
    };

    public int Dimensions => AiClientFactory.DeterministicEmbeddingDimensions;

    public Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default)
        => Task.FromResult(Embed(text));

    public Task<IReadOnlyList<float[]>> EmbedBatchAsync(
        IReadOnlyList<string> texts,
        CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<float[]>>(texts.Select(Embed).ToList());

    private float[] Embed(string text)
    {
        var vector = new float[Dimensions];
        var tokens = Tokenize(text);

        for (var i = 0; i < tokens.Count; i++)
        {
            vector[BucketOf(tokens[i])] += 1f;

            if (i + 1 < tokens.Count)
            {
                vector[BucketOf($"{tokens[i]}_{tokens[i + 1]}")] += BigramWeight;
            }
        }

        Normalize(vector);
        return vector;
    }

    private int BucketOf(string term) => (int)(Fnv1A(term) % (uint)Dimensions);

    /// <summary>
    /// FNV-1a. <see cref="string.GetHashCode()"/> is randomized per process, which would make
    /// stored vectors unreadable after a restart.
    /// </summary>
    private static uint Fnv1A(string value)
    {
        const uint offsetBasis = 2166136261;
        const uint prime = 16777619;

        var hash = offsetBasis;
        foreach (var b in Encoding.UTF8.GetBytes(value))
        {
            hash = (hash ^ b) * prime;
        }

        return hash;
    }

    private static void Normalize(float[] vector)
    {
        var magnitude = MathF.Sqrt(vector.Sum(v => v * v));
        if (magnitude == 0f)
        {
            return;
        }

        for (var i = 0; i < vector.Length; i++)
        {
            vector[i] /= magnitude;
        }
    }

    private static List<string> Tokenize(string text)
    {
        var tokens = new List<string>();
        var current = new StringBuilder();

        foreach (var ch in text)
        {
            if (char.IsLetterOrDigit(ch))
            {
                current.Append(char.ToLowerInvariant(ch));
                continue;
            }

            AddToken(tokens, current);
        }

        AddToken(tokens, current);
        return tokens;
    }

    private static void AddToken(List<string> tokens, StringBuilder buffer)
    {
        if (buffer.Length == 0)
        {
            return;
        }

        var token = buffer.ToString();
        buffer.Clear();

        if (token.Length >= MinimumTokenLength && !StopWords.Contains(token))
        {
            tokens.Add(Stem(token));
        }
    }

    /// <summary>
    /// Crude suffix stripping so "products" and "product" share a bucket. A real stemmer would be
    /// better, but this covers the plural and gerund forms that dominate policy documents.
    /// </summary>
    private static string Stem(string token) => token switch
    {
        { Length: > 4 } when token.EndsWith("ies", StringComparison.Ordinal) => token[..^3] + "y",
        { Length: > 4 } when token.EndsWith("sses", StringComparison.Ordinal) => token[..^2],
        { Length: > 4 } when token.EndsWith("ing", StringComparison.Ordinal) => token[..^3],
        { Length: > 3 } when token.EndsWith("ed", StringComparison.Ordinal) => token[..^2],
        { Length: > 3 } when token.EndsWith('s') && !token.EndsWith("ss", StringComparison.Ordinal) => token[..^1],
        _ => token,
    };
}
