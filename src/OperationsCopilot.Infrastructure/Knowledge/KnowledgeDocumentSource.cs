using System.Reflection;

namespace OperationsCopilot.Infrastructure.Knowledge;

/// <summary>One source document, ready to be chunked.</summary>
public sealed record KnowledgeDocument(string FileName, string Markdown);

/// <summary>Supplies the Markdown documents that make up the knowledge base.</summary>
public interface IKnowledgeDocumentSource
{
    Task<IReadOnlyList<KnowledgeDocument>> LoadAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Reads the knowledge base from files embedded in this assembly at build time
/// (see the <c>EmbeddedResource</c> item in the project file).
/// </summary>
/// <remarks>
/// Embedding rather than reading from disk means the container image carries the documents and
/// there is no path to get wrong between local runs, Docker, and CI. Swap this implementation for
/// a blob or SharePoint reader to point the same pipeline at real content.
/// </remarks>
public sealed class EmbeddedKnowledgeDocumentSource : IKnowledgeDocumentSource
{
    private const string ResourcePrefix = "OperationsCopilot.Infrastructure.KnowledgeBase.";

    public async Task<IReadOnlyList<KnowledgeDocument>> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        var assembly = typeof(EmbeddedKnowledgeDocumentSource).Assembly;
        var documents = new List<KnowledgeDocument>();

        foreach (var resourceName in assembly.GetManifestResourceNames()
                     .Where(name => name.StartsWith(ResourcePrefix, StringComparison.Ordinal)
                         && name.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
                     .OrderBy(name => name, StringComparer.Ordinal))
        {
            var markdown = await ReadResourceAsync(assembly, resourceName, cancellationToken);
            documents.Add(new KnowledgeDocument(resourceName[ResourcePrefix.Length..], markdown));
        }

        return documents;
    }

    private static async Task<string> ReadResourceAsync(
        Assembly assembly,
        string resourceName,
        CancellationToken cancellationToken)
    {
        await using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded knowledge base resource '{resourceName}' could not be opened.");

        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync(cancellationToken);
    }
}
