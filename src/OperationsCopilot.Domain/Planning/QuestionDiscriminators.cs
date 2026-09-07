namespace OperationsCopilot.Domain.Planning;

/// <summary>
/// Pulls out the tokens that say <em>which</em> thing a question is about, as opposed to what
/// kind of question it is.
/// </summary>
/// <remarks>
/// <para>
/// This exists because similarity alone cannot make plan reuse safe, and that is a measured
/// claim rather than a cautious one. Against nomic-embed-text, question pairs that differ only in
/// their subject score right in the middle of the paraphrase band:
/// </para>
/// <code>
/// 0.9074  revenue in the last 30 days     | revenue in the last 90 days
/// 0.8681  Tell me about PT-1001.          | Tell me about PT-1006.
/// 0.8588  approve a 15% discount          | approve a 25% discount
/// </code>
/// <para>
/// All three sit above any floor that still admits genuine paraphrases, so a purely vector-based
/// cache would answer about the wrong product, period, or discount band — confidently, because
/// the tools would run and return real figures for the wrong question. The floor separates
/// subject matter; this separates subjects.
/// </para>
/// <para>
/// The rule errs towards rejection on purpose. A discriminator found where none was meant costs
/// one cache miss, which is one model round trip. A discriminator missed costs a wrong answer.
/// </para>
/// </remarks>
public static class QuestionDiscriminators
{
    private static readonly char[] Punctuation =
        ['.', ',', '?', '!', ':', ';', '"', '\'', '(', ')', '[', ']', '…', '’'];

    /// <summary>True when two questions are about the same subjects and may share a plan.</summary>
    public static bool Match(string question, string other) =>
        Extract(question).SetEquals(Extract(other));

    /// <summary>
    /// Numbers, codes, and proper nouns, lower-cased so that <c>PT-1001</c> and <c>pt-1001</c>
    /// count as the same subject.
    /// </summary>
    public static HashSet<string> Extract(string question)
    {
        var discriminators = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(question))
        {
            return discriminators;
        }

        var atSentenceStart = true;

        foreach (var word in question.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            var token = word.Trim(Punctuation);

            if (token.Length > 0 && IsDiscriminator(token, atSentenceStart))
            {
                discriminators.Add(token);
            }

            atSentenceStart = word.EndsWith('.') || word.EndsWith('?') || word.EndsWith('!');
        }

        return discriminators;
    }

    private static bool IsDiscriminator(string token, bool atSentenceStart)
    {
        // Anything carrying a digit names a specific thing: a SKU (PT-1001), a warehouse
        // (WH-EU-01), a window (30), a rate (15%).
        if (token.Any(char.IsDigit))
        {
            return true;
        }

        // A proper noun: EMEA, Rotterdam, Torqline. Capitalisation at the start of a sentence
        // carries no information, so it is not counted there — otherwise every question would
        // discriminate on its own first word.
        return !atSentenceStart && char.IsUpper(token[0]);
    }
}
