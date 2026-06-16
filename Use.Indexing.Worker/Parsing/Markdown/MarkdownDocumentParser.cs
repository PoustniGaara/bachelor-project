using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Use.Indexing.Worker.Models;

namespace Use.Indexing.Worker.Parsing.Markdown;

/// <summary>
/// Markdown parser that produces both flattened plain text and a hierarchical
/// <see cref="DocumentOutline"/>. Walks the Markdig AST once; converts each
/// block kind to clean plain text using <see cref="MarkdownPlainTextRenderer"/>.
/// </summary>
public sealed class MarkdownDocumentParser
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UsePipeTables()
        .UseGridTables()
        .UseAutoLinks()
        .UseEmphasisExtras()
        .Build();

    public (string PlainText, DocumentOutline Outline) Parse(string markdown, string title)
    {
        var ast = Markdig.Markdown.Parse(markdown, Pipeline);
        var builder = new OutlineBuilder(title);

        foreach (var node in ast)
        {
            switch (node)
            {
                case HeadingBlock h:
                    builder.OpenSection(h.Level, MarkdownPlainTextRenderer.Inline(h.Inline));
                    break;
                case ParagraphBlock p:
                    builder.AddBlock(DocumentBlockKind.Paragraph,
                        MarkdownPlainTextRenderer.Inline(p.Inline));
                    break;
                case ListBlock l:
                    builder.AddBlock(DocumentBlockKind.List,
                        MarkdownPlainTextRenderer.List(l));
                    break;
                case Markdig.Extensions.Tables.Table t:
                    builder.AddBlock(DocumentBlockKind.Table,
                        MarkdownPlainTextRenderer.Table(t));
                    break;
                case FencedCodeBlock c:
                    builder.AddBlock(DocumentBlockKind.Code,
                        c.Lines.ToString(), language: c.Info);
                    break;
                case QuoteBlock q:
                    builder.AddBlock(DocumentBlockKind.Quote,
                        MarkdownPlainTextRenderer.Quote(q));
                    break;
                // ThematicBreaks, HtmlBlocks, etc. → ignored or stripped.
            }
        }

        return (builder.BuildPlainText(), builder.BuildOutline());
    }
}