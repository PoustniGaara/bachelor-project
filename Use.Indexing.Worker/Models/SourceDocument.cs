namespace Use.Indexing.Worker.Models;

/// <summary>
/// Raw document fetched from a source system, before parsing/normalization.
/// <see cref="RawContent"/> is content in its native format (HTML, Markdown,
/// .docx bytes encoded as base64, etc.) and <see cref="ContentType"/> tells
/// the parser how to interpret it.
/// </summary>
public sealed record SourceDocument(
    SourceDocumentReference Reference,
    string ContentType,
    string RawContent,
    IReadOnlyDictionary<string, string> Metadata)
{
    public static SourceDocument Empty(SourceDocumentReference reference) =>
        new(reference, "text/plain", string.Empty,
            new Dictionary<string, string>());
}

