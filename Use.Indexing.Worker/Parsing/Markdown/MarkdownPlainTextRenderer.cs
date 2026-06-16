using System.Text;
using Markdig.Extensions.Tables;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace Use.Indexing.Worker.Parsing.Markdown;

/// <summary>
/// Converts Markdig AST fragments (inlines, lists, tables, quotes) into clean
/// plain text suitable for embeddings. Hyperlinks are rendered as
/// "label (url)" so URLs survive into the vector space; tables are flattened
/// row-by-row so each row becomes an independently retrievable sentence.
/// </summary>
internal static class MarkdownPlainTextRenderer
{
    public static string Inline(ContainerInline? container)
    {
        if (container is null) return string.Empty;
        var sb = new StringBuilder();
        AppendInlines(sb, container);
        return CollapseWhitespace(sb.ToString());
    }

    public static string List(ListBlock list)
    {
        var sb = new StringBuilder();
        RenderList(sb, list, depth: 0);
        return sb.ToString().TrimEnd();
    }

    public static string Table(Table table)
    {
        // Flatten to "header1: cell1; header2: cell2" rows when a header row
        // exists, or to "cell1; cell2; cell3" otherwise. This keeps each row
        // self-describing for retrieval.
        var rows = table.OfType<TableRow>().ToList();
        if (rows.Count == 0) return string.Empty;

        var headers = rows[0].IsHeader
            ? rows[0].OfType<TableCell>().Select(CellText).ToList()
            : new List<string>();

        var dataRows = rows[0].IsHeader ? rows.Skip(1) : rows;
        var sb = new StringBuilder();

        foreach (var row in dataRows)
        {
            var cells = row.OfType<TableCell>().Select(CellText).ToList();
            if (cells.All(string.IsNullOrWhiteSpace)) continue;

            if (headers.Count > 0)
            {
                var pairs = cells.Select((c, i) =>
                {
                    var header = i < headers.Count ? headers[i] : $"col{i + 1}";
                    return string.IsNullOrWhiteSpace(header) ? c : $"{header}: {c}";
                });
                sb.AppendLine(string.Join("; ", pairs.Where(s => !string.IsNullOrWhiteSpace(s))));
            }
            else
            {
                sb.AppendLine(string.Join("; ", cells.Where(s => !string.IsNullOrWhiteSpace(s))));
            }
        }

        return sb.ToString().TrimEnd();
    }

    public static string Quote(QuoteBlock quote)
    {
        var sb = new StringBuilder();
        foreach (var child in quote)
        {
            switch (child)
            {
                case ParagraphBlock p:
                    sb.AppendLine(Inline(p.Inline));
                    break;
                case ListBlock l:
                    sb.AppendLine(List(l));
                    break;
                case QuoteBlock q:
                    sb.AppendLine(Quote(q));
                    break;
            }
        }
        return sb.ToString().TrimEnd();
    }

    // ----- private helpers -----

    private static void AppendInlines(StringBuilder sb, ContainerInline container)
    {
        foreach (var inline in container)
        {
            switch (inline)
            {
                case LiteralInline literal:
                    sb.Append(literal.Content.ToString());
                    break;
                case CodeInline code:
                    sb.Append(code.Content);
                    break;
                case LineBreakInline:
                    sb.Append(' ');
                    break;
                case LinkInline link:
                {
                    var label = new StringBuilder();
                    AppendInlines(label, link);
                    var labelText = label.ToString().Trim();
                    var url = link.Url ?? string.Empty;

                    if (link.IsImage)
                    {
                        // For images keep the alt text only — URLs to binary
                        // assets aren't useful for text retrieval.
                        if (!string.IsNullOrWhiteSpace(labelText)) sb.Append(labelText);
                    }
                    else if (string.IsNullOrWhiteSpace(labelText) || labelText == url)
                    {
                        sb.Append(url);
                    }
                    else
                    {
                        sb.Append(labelText).Append(" (").Append(url).Append(')');
                    }
                    break;
                }
                case AutolinkInline auto:
                    sb.Append(auto.Url);
                    break;
                case HtmlInline:
                case HtmlEntityInline:
                    // Drop raw HTML — Wiki.js content uses things like <br>.
                    sb.Append(' ');
                    break;
                case ContainerInline nested:
                    AppendInlines(sb, nested);
                    break;
                default:
                    // Unknown inline — best-effort fallback.
                    sb.Append(' ');
                    break;
            }
        }
    }

    private static void RenderList(StringBuilder sb, ListBlock list, int depth)
    {
        var indent = new string(' ', depth * 2);
        var ordered = list.IsOrdered;
        var index = 1;

        foreach (var item in list.OfType<ListItemBlock>())
        {
            var bullet = ordered ? $"{index}." : "-";
            sb.Append(indent).Append(bullet).Append(' ');

            var first = true;
            foreach (var child in item)
            {
                switch (child)
                {
                    case ParagraphBlock p:
                        if (!first) sb.Append(' ');
                        sb.Append(Inline(p.Inline));
                        first = false;
                        break;
                    case ListBlock nested:
                        sb.AppendLine();
                        RenderList(sb, nested, depth + 1);
                        first = false;
                        break;
                }
            }
            sb.AppendLine();
            index++;
        }
    }

    private static string CellText(TableCell cell)
    {
        var sb = new StringBuilder();
        foreach (var block in cell)
        {
            if (block is ParagraphBlock p)
            {
                if (sb.Length > 0) sb.Append(' ');
                sb.Append(Inline(p.Inline));
            }
        }
        return sb.ToString().Trim();
    }

    private static string CollapseWhitespace(string input)
    {
        if (string.IsNullOrEmpty(input)) return string.Empty;
        var sb = new StringBuilder(input.Length);
        var lastWasSpace = false;
        foreach (var ch in input)
        {
            if (char.IsWhiteSpace(ch))
            {
                if (!lastWasSpace) sb.Append(' ');
                lastWasSpace = true;
            }
            else
            {
                sb.Append(ch);
                lastWasSpace = false;
            }
        }
        return sb.ToString().Trim();
    }
}

