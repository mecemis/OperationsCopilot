using System.Text;

namespace OperationsCopilot.Infrastructure.Knowledge;

/// <summary>A chunk of a Markdown document, before it has been embedded.</summary>
/// <param name="EmbeddingInput">
/// What actually gets embedded. The heading is prepended to the body so that a chunk carries its
/// own topic; without it, a chunk that says "up to 5%" retrieves poorly because nothing in the
/// text says what the number is about.
/// </param>
public sealed record MarkdownChunk(
    string DocumentTitle,
    string Heading,
    int ChunkIndex,
    string Content,
    string EmbeddingInput);

/// <summary>
/// Splits Markdown into retrieval-sized chunks along its own structure: first by section
/// heading, then by paragraph when a section is longer than the target size.
/// </summary>
/// <remarks>
/// Splitting on headings rather than a fixed character window is what makes citations precise —
/// every chunk can name the section it came from, so an answer points at
/// "Returns and Warranty Policy &gt; Warranty Terms by Category" instead of "chunk 7".
/// </remarks>
public sealed class MarkdownChunker(int maxChunkCharacters = 900, int overlapCharacters = 150)
{
    private readonly int _maxChunkCharacters = Math.Max(200, maxChunkCharacters);
    private readonly int _overlapCharacters = Math.Clamp(overlapCharacters, 0, Math.Max(200, maxChunkCharacters) / 2);

    public IReadOnlyList<MarkdownChunk> Chunk(string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return [];
        }

        var sections = SplitIntoSections(markdown);
        var documentTitle = sections.FirstOrDefault(s => s.Level == 1)?.Heading ?? "Untitled document";

        var chunks = new List<MarkdownChunk>();

        foreach (var section in sections)
        {
            if (string.IsNullOrWhiteSpace(section.Body))
            {
                continue;
            }

            foreach (var body in SplitToSize(section.Body))
            {
                chunks.Add(new MarkdownChunk(
                    documentTitle,
                    section.Heading,
                    chunks.Count,
                    body,
                    BuildEmbeddingInput(documentTitle, section.Heading, body)));
            }
        }

        return chunks;
    }

    private static string BuildEmbeddingInput(string documentTitle, string heading, string body)
        => documentTitle.Equals(heading, StringComparison.Ordinal)
            ? $"{documentTitle}\n\n{body}"
            : $"{documentTitle} — {heading}\n\n{body}";

    private sealed record Section(string Heading, int Level, string Body);

    private static List<Section> SplitIntoSections(string markdown)
    {
        var sections = new List<Section>();
        var currentHeading = "Introduction";
        var currentLevel = 1;
        var body = new StringBuilder();

        foreach (var line in markdown.ReplaceLineEndings("\n").Split('\n'))
        {
            var level = HeadingLevel(line);
            if (level == 0)
            {
                body.AppendLine(line);
                continue;
            }

            if (body.Length > 0 || sections.Count > 0)
            {
                sections.Add(new Section(currentHeading, currentLevel, body.ToString().Trim()));
            }

            currentHeading = line[level..].Trim();
            currentLevel = level;
            body.Clear();
        }

        sections.Add(new Section(currentHeading, currentLevel, body.ToString().Trim()));
        return sections.Where(s => s.Body.Length > 0).ToList();
    }

    /// <summary>Returns the ATX heading level (1-6), or 0 when the line is not a heading.</summary>
    private static int HeadingLevel(string line)
    {
        var hashes = 0;
        while (hashes < line.Length && line[hashes] == '#')
        {
            hashes++;
        }

        var isHeading = hashes is > 0 and <= 6
            && hashes < line.Length
            && line[hashes] == ' ';

        return isHeading ? hashes : 0;
    }

    /// <summary>
    /// Packs paragraphs into chunks up to the target size, carrying the tail of each chunk into
    /// the next so a rule split across a boundary is still retrievable from either side.
    /// </summary>
    private IEnumerable<string> SplitToSize(string body)
    {
        if (body.Length <= _maxChunkCharacters)
        {
            yield return body;
            yield break;
        }

        var paragraphs = body.Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var buffer = new StringBuilder();

        foreach (var paragraph in paragraphs)
        {
            if (buffer.Length > 0 && buffer.Length + paragraph.Length + 2 > _maxChunkCharacters)
            {
                var chunk = buffer.ToString().Trim();
                yield return chunk;

                buffer.Clear();
                if (_overlapCharacters > 0 && chunk.Length > _overlapCharacters)
                {
                    buffer.Append(chunk[^_overlapCharacters..]).Append("\n\n");
                }
            }

            buffer.Append(paragraph).Append("\n\n");
        }

        var tail = buffer.ToString().Trim();
        if (tail.Length > 0)
        {
            yield return tail;
        }
    }
}
