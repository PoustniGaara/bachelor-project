namespace Use.Indexing.Worker.Models;

/// <summary>
/// Hierarchical, language-agnostic outline of a document. Produced by the
/// parser, consumed by structure-aware chunkers. Heading levels follow
/// Markdown conventions (1 = top, 6 = deepest). The synthetic root has Level=0.
/// </summary>
public sealed record DocumentOutline(DocumentSection Root);

public sealed record DocumentSection(
    int Level,
    string Heading,
    IReadOnlyList<string> HeadingPath,   // breadcrumbs incl. current heading
    IReadOnlyList<DocumentBlock> Blocks, // direct content (paragraphs, lists, tables, code)
    IReadOnlyList<DocumentSection> Children);

public enum DocumentBlockKind
{
    Paragraph,
    List,
    Table,
    Code,
    Quote
}

/// <summary>
/// A leaf piece of content already converted to plain text (Markdown stripped).
/// Tables are flattened to a readable, line-based form so embeddings see rows
/// as coherent sentences instead of pipe-separated noise.
/// </summary>
public sealed record DocumentBlock(
    DocumentBlockKind Kind,
    string Text,
    int CharacterLength,
    string? Language = null); // for code blocks