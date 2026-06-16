using System.Text.RegularExpressions;
using Use.Indexing.Worker.Models;
using Use.Indexing.Worker.Parsing.Markdown;

namespace Use.Indexing.Worker.Parsing;

/// <summary>
/// Default best-effort parser. Routes by content type:
///   - text/markdown → <see cref="MarkdownDocumentParser"/> (full outline + plain text)
///   - text/html     → naive tag-strip placeholder (TODO: AngleSharp)
///   - everything else → treated as plain text
/// Producing a <see cref="DocumentOutline"/> is best-effort: only Markdown
/// currently emits one. Downstream chunkers must tolerate <c>Outline = null</c>.
/// </summary>
public sealed class DefaultDocumentParser : IDocumentParser
{
    private static readonly Regex HtmlTagRegex = new("<[^>]+>", RegexOptions.Compiled);

    private readonly MarkdownDocumentParser _markdown;

    public DefaultDocumentParser(MarkdownDocumentParser markdown) => _markdown = markdown;

    public Task<ParsedDocument> ParseAsync(SourceDocument doc, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(doc.ContentType.ToLowerInvariant() switch
        {
            "text/markdown" => ParseMarkdown(doc),
            "text/html"     => ParseHtml(doc),
            _               => ParsePlain(doc)
        });
    }

    private ParsedDocument ParseMarkdown(SourceDocument doc)
    {
        var (plain, outline) = _markdown.Parse(doc.RawContent, doc.Reference.Title);
        return new ParsedDocument(
            doc.Reference,
            doc.Reference.Title,
            plain,
            Tags: Array.Empty<string>(),
            Metadata: doc.Metadata,
            Outline: outline);
    }

    private static ParsedDocument ParseHtml(SourceDocument doc)
    {
        // Placeholder: replace tags with spaces. Production should use AngleSharp
        // and emit a DocumentOutline analogous to the Markdown path.
        var text = HtmlTagRegex.Replace(doc.RawContent, " ");
        return new ParsedDocument(
            doc.Reference,
            doc.Reference.Title,
            text,
            Tags: Array.Empty<string>(),
            Metadata: doc.Metadata);
    }

    private static ParsedDocument ParsePlain(SourceDocument doc) =>
        new(doc.Reference,
            doc.Reference.Title,
            doc.RawContent,
            Tags: Array.Empty<string>(),
            Metadata: doc.Metadata);
}

