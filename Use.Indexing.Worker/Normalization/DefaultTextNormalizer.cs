using System.Text;
using System.Text.RegularExpressions;
using Use.Indexing.Worker.Models;
using Use.Indexing.Worker.Parsing;

namespace Use.Indexing.Worker.Normalization;

/// <summary>
/// Default text normalizer. Trims, collapses whitespace, NFC-normalizes both
/// the flat <see cref="ParsedDocument.Text"/> and every <see cref="DocumentBlock"/>
/// inside the optional <see cref="DocumentOutline"/>. Empty blocks/sections are
/// pruned so chunkers never have to defend against whitespace-only inputs.
/// </summary>
public sealed class DefaultTextNormalizer : ITextNormalizer
{
    private static readonly Regex HorizontalWhitespace = new(@"[ \t]+", RegexOptions.Compiled);
    private static readonly Regex BlankLines = new(@"(\r?\n[ \t]*){2,}", RegexOptions.Compiled);
    private static readonly Regex AllWhitespace = new(@"\s+", RegexOptions.Compiled);

    public NormalizedDocument Normalize(ParsedDocument parsed)
    {
        var plain = CleanFlat(parsed.Text);
        var outline = parsed.Outline is null ? null : NormalizeOutline(parsed.Outline);

        return new NormalizedDocument(
            parsed.Reference,
            parsed.Title?.Trim() ?? string.Empty,
            plain,
            parsed.Tags,
            parsed.Metadata,
            Permissions: null,
            Outline: outline);
    }

    private static DocumentOutline NormalizeOutline(DocumentOutline outline)
        => new(NormalizeSection(outline.Root));

    private static DocumentSection NormalizeSection(DocumentSection s)
    {
        var blocks = s.Blocks
            .Select(NormalizeBlock)
            .Where(b => !string.IsNullOrWhiteSpace(b.Text))
            .ToList();

        var children = s.Children
            .Select(NormalizeSection)
            .Where(c => c.Blocks.Count > 0 || c.Children.Count > 0)
            .ToList();

        return new DocumentSection(
            s.Level,
            (s.Heading ?? string.Empty).Trim(),
            s.HeadingPath.Select(h => h.Trim()).ToList(),
            blocks,
            children);
    }

    private static DocumentBlock NormalizeBlock(DocumentBlock b)
    {
        // Code blocks preserve internal layout; other kinds get spaces collapsed
        // but newlines preserved as soft split points for the chunker.
        var text = b.Kind == DocumentBlockKind.Code
            ? b.Text.Trim().Normalize(NormalizationForm.FormC)
            : CleanStructured(b.Text);

        return b with { Text = text, CharacterLength = text.Length };
    }

    private static string CleanFlat(string input)
    {
        if (string.IsNullOrEmpty(input)) return string.Empty;
        var noNbsp = input.Replace('\u00A0', ' ');
        return AllWhitespace.Replace(noNbsp, " ").Trim().Normalize(NormalizationForm.FormC);
    }

    private static string CleanStructured(string input)
    {
        if (string.IsNullOrEmpty(input)) return string.Empty;
        var noNbsp = input.Replace('\u00A0', ' ');
        var collapsedSpaces = HorizontalWhitespace.Replace(noNbsp, " ");
        var collapsedBlankLines = BlankLines.Replace(collapsedSpaces, "\n\n");
        return collapsedBlankLines.Trim().Normalize(NormalizationForm.FormC);
    }
}

