using Use.Indexing.Worker.Models;

namespace Use.Indexing.Worker.Parsing;

/// <summary>
/// Converts a <see cref="SourceDocument"/> (any content type) into raw text
/// plus structural metadata. Implementations may dispatch to format-specific
/// parsers (HTML, Markdown, .docx, PDF, ...).
/// </summary>
public interface IDocumentParser
{
    Task<ParsedDocument> ParseAsync(SourceDocument document, CancellationToken cancellationToken);
}

public sealed record ParsedDocument(
    SourceDocumentReference Reference,
    string Title,
    string Text,
    IReadOnlyList<string> Tags,
    IReadOnlyDictionary<string, string> Metadata,
    DocumentOutline? Outline = null);

