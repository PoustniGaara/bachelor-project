using System.Text;
using Use.Indexing.Worker.Models;

namespace Use.Indexing.Worker.Parsing.Markdown;

/// <summary>
/// Accumulates a hierarchical <see cref="DocumentOutline"/> while the parser
/// streams blocks in document order. Maintains a stack of mutable section
/// frames keyed by heading level: a new heading of level L pops every frame
/// with Level &gt;= L before opening a fresh one. Also produces the flattened
/// plain-text representation used as a fallback / lexical search blob.
/// </summary>
internal sealed class OutlineBuilder
{
    private readonly SectionFrame _root;
    private readonly Stack<SectionFrame> _stack = new();
    private readonly StringBuilder _plainText = new();

    public OutlineBuilder(string documentTitle)
    {
        _root = new SectionFrame(level: 0, heading: documentTitle ?? string.Empty,
            headingPath: Array.Empty<string>());
        _stack.Push(_root);
    }

    /// <summary>Open a new section, popping any deeper-or-equal frames first.</summary>
    public void OpenSection(int level, string heading)
    {
        var safeLevel = Math.Clamp(level, 1, 6);

        while (_stack.Count > 1 && _stack.Peek().Level >= safeLevel)
            _stack.Pop();

        var parent = _stack.Peek();
        var path = parent.HeadingPath.Concat(new[] { heading }).ToList();
        var frame = new SectionFrame(safeLevel, heading, path);
        parent.Children.Add(frame);
        _stack.Push(frame);

        if (_plainText.Length > 0) _plainText.AppendLine().AppendLine();
        _plainText.Append(heading);
    }

    /// <summary>Append a leaf block (paragraph, list, table, code, quote) to the current section.</summary>
    public void AddBlock(DocumentBlockKind kind, string text, string? language = null)
    {
        if (string.IsNullOrWhiteSpace(text)) return;

        var block = new DocumentBlock(kind, text, text.Length, language);
        _stack.Peek().Blocks.Add(block);

        if (_plainText.Length > 0) _plainText.AppendLine().AppendLine();
        _plainText.Append(text);
    }

    public string BuildPlainText() => _plainText.ToString().Trim();

    public DocumentOutline BuildOutline() => new(_root.Freeze());

    // ----- mutable frame -----

    private sealed class SectionFrame
    {
        public int Level { get; }
        public string Heading { get; }
        public IReadOnlyList<string> HeadingPath { get; }
        public List<DocumentBlock> Blocks { get; } = new();
        public List<SectionFrame> Children { get; } = new();

        public SectionFrame(int level, string heading, IReadOnlyList<string> headingPath)
        {
            Level = level;
            Heading = heading;
            HeadingPath = headingPath;
        }

        public DocumentSection Freeze() => new(
            Level,
            Heading,
            HeadingPath,
            Blocks,
            Children.Select(c => c.Freeze()).ToList());
    }
}

