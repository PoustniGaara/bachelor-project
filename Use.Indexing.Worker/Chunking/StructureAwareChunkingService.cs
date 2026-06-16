using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Use.Indexing.Worker.Configuration;
using Use.Indexing.Worker.Models;

namespace Use.Indexing.Worker.Chunking;

/// <summary>
/// Splits documents along their semantic structure (sections → blocks →
/// sentences). Each chunk carries a heading-path breadcrumb header so the
/// embedding sees topical context, and small adjacent sections are merged to
/// avoid low-signal chunks. Falls back to the character-window chunker when
/// no <see cref="DocumentOutline"/> is available.
/// </summary>
public sealed class StructureAwareChunkingService : IChunkingService
{
    private readonly ChunkingOptions _opts;
    private readonly CharacterWindowChunkingService _fallback;

    public StructureAwareChunkingService(
        IOptions<IndexingOptions> options,
        CharacterWindowChunkingService fallback)
    {
        _opts = options.Value.Chunking;
        _fallback = fallback;
    }

    public IReadOnlyList<DocumentChunk> Chunk(NormalizedDocument document)
    {
        if (document.Outline is null)
            return _fallback.Chunk(document);

        var sink = new ChunkSink(document, _opts);
        WalkSection(document.Outline.Root, sink);
        sink.FlushPending();
        return sink.Build();
    }

    private void WalkSection(DocumentSection section, ChunkSink sink)
    {
        if (section.Level > _opts.MaxHeadingDepth)
        {
            // Collapse very-deep sections into the parent's stream by treating
            // their blocks as belonging to the nearest ancestor still in scope.
            sink.AbsorbDeepSection(section);
            return;
        }

        if (section.Blocks.Count > 0)
            sink.EmitSection(section);

        foreach (var child in section.Children)
            WalkSection(child, sink);
    }
}

/// <summary>
/// Encapsulates merge-small / split-large policy. Kept separate from the
/// recursive walker so each method stays small and unit-testable.
/// </summary>
internal sealed class ChunkSink
{
    private static readonly Regex SentenceSplit =
        new(@"(?<=[\.\!\?…])\s+(?=[\p{Lu}\p{N}])", RegexOptions.Compiled);

    private readonly NormalizedDocument _doc;
    private readonly ChunkingOptions _opts;
    private readonly List<DocumentChunk> _chunks = new();

    private readonly StringBuilder _pendingText = new();
    private IReadOnlyList<string>? _pendingHeadingPath;
    private int _pendingDeepestLevel;
    private readonly HashSet<DocumentBlockKind> _pendingKinds = new();

    public ChunkSink(NormalizedDocument doc, ChunkingOptions opts)
    {
        _doc = doc;
        _opts = opts;
    }

    public void EmitSection(DocumentSection section)
    {
        var body = JoinBlocks(section.Blocks);
        var withHeader = WithBreadcrumb(section.HeadingPath, body);
        var kinds = section.Blocks.Select(b => b.Kind).ToHashSet();

        if (withHeader.Length < _opts.MinCharacters)
        {
            BufferForMerge(section.HeadingPath, section.Level, kinds, withHeader);
            return;
        }

        FlushPending();

        if (withHeader.Length <= _opts.TargetCharacters)
        {
            Add(withHeader, section.HeadingPath, section.Level, kinds);
            return;
        }

        foreach (var piece in SplitLarge(section.HeadingPath, section.Blocks))
            Add(piece, section.HeadingPath, section.Level, kinds);
    }

    public void AbsorbDeepSection(DocumentSection section)
    {
        if (section.Blocks.Count == 0) return;

        var path = TrimPath(section.HeadingPath, _opts.MaxHeadingDepth);
        var body = JoinBlocks(section.Blocks);
        var kinds = section.Blocks.Select(b => b.Kind).ToHashSet();

        BufferForMerge(path, _opts.MaxHeadingDepth, kinds, WithBreadcrumb(path, body));
    }

    public void FlushPending()
    {
        if (_pendingText.Length == 0) return;

        var text = _pendingText.ToString().Trim();
        var path = _pendingHeadingPath ?? Array.Empty<string>();
        var kinds = _pendingKinds.ToHashSet();
        var level = _pendingDeepestLevel;

        ResetPending();

        if (text.Length <= _opts.TargetCharacters)
        {
            Add(text, path, level, kinds);
            return;
        }

        foreach (var piece in SplitText(text, _opts.TargetCharacters, _opts.Overlap))
            Add(piece, path, level, kinds);
    }

    public IReadOnlyList<DocumentChunk> Build() => _chunks;

    // ---------- private ----------

    private void BufferForMerge(
        IReadOnlyList<string> path,
        int level,
        IEnumerable<DocumentBlockKind> kinds,
        string text)
    {
        if (_pendingHeadingPath is not null &&
            !PathsShareAncestor(_pendingHeadingPath, path))
        {
            FlushPending();
        }

        if (_pendingText.Length > 0) _pendingText.Append("\n\n");
        _pendingText.Append(text);

        _pendingHeadingPath ??= path;
        _pendingDeepestLevel = Math.Max(_pendingDeepestLevel, level);
        foreach (var k in kinds) _pendingKinds.Add(k);

        if (_pendingText.Length >= _opts.TargetCharacters)
            FlushPending();
    }

    private void ResetPending()
    {
        _pendingText.Clear();
        _pendingHeadingPath = null;
        _pendingDeepestLevel = 0;
        _pendingKinds.Clear();
    }

    private IEnumerable<string> SplitLarge(
        IReadOnlyList<string> headingPath,
        IReadOnlyList<DocumentBlock> blocks)
    {
        var header = BreadcrumbHeader(headingPath);
        var headerLen = header.Length;
        var budget = Math.Max(_opts.TargetCharacters - headerLen, _opts.MinCharacters);

        var current = new StringBuilder();

        foreach (var block in blocks)
        {
            // A single block bigger than the budget must be sentence-split.
            if (block.Text.Length > budget)
            {
                if (current.Length > 0)
                {
                    yield return Compose(header, current.ToString());
                    current.Clear();
                }

                foreach (var piece in SplitText(block.Text, budget, _opts.Overlap))
                    yield return Compose(header, piece);
                continue;
            }

            if (current.Length + block.Text.Length + 2 > budget && current.Length > 0)
            {
                yield return Compose(header, current.ToString());
                current.Clear();
            }

            if (current.Length > 0) current.Append("\n\n");
            current.Append(block.Text);
        }

        if (current.Length > 0)
            yield return Compose(header, current.ToString());
    }

    private static IEnumerable<string> SplitText(string text, int target, int overlap)
    {
        var sentences = SentenceSplit.Split(text);
        var current = new StringBuilder();

        foreach (var sentence in sentences)
        {
            if (sentence.Length > target)
            {
                if (current.Length > 0)
                {
                    yield return current.ToString().Trim();
                    current.Clear();
                }
                // Hard fall-through: window this oversized sentence.
                foreach (var window in HardWindow(sentence, target, overlap))
                    yield return window;
                continue;
            }

            if (current.Length + sentence.Length + 1 > target && current.Length > 0)
            {
                var emitted = current.ToString().Trim();
                yield return emitted;

                current.Clear();
                if (overlap > 0 && emitted.Length > overlap)
                    current.Append(emitted[^overlap..]).Append(' ');
            }

            if (current.Length > 0) current.Append(' ');
            current.Append(sentence);
        }

        if (current.Length > 0)
            yield return current.ToString().Trim();
    }

    private static IEnumerable<string> HardWindow(string text, int size, int overlap)
    {
        var step = Math.Max(1, size - Math.Clamp(overlap, 0, size / 2));
        for (var start = 0; start < text.Length; start += step)
        {
            var len = Math.Min(size, text.Length - start);
            yield return text.Substring(start, len);
            if (start + len >= text.Length) yield break;
        }
    }

    private void Add(
        string text,
        IReadOnlyList<string> headingPath,
        int headingLevel,
        IEnumerable<DocumentBlockKind> kinds)
    {
        var trimmed = text.Trim();
        if (trimmed.Length == 0) return;

        var order = _chunks.Count;
        var chunkId = $"{_doc.Reference.SourceSystem}:{_doc.Reference.SourceDocumentId}:{order}";

        var meta = new Dictionary<string, string>(_doc.Metadata)
        {
            ["title"]         = _doc.Title,
            ["sourceUrl"]     = _doc.Reference.Url,
            ["chunkOrder"]    = order.ToString(),
            ["headingPath"]   = string.Join(" › ", headingPath),
            ["headingLevel"]  = headingLevel.ToString(),
            ["blockKinds"]    = string.Join(",", kinds.Select(k => k.ToString()).Distinct().OrderBy(x => x))
        };

        _chunks.Add(new DocumentChunk(chunkId, _doc.Reference, order, trimmed, meta));
    }

    // ---------- helpers ----------

    private static string JoinBlocks(IReadOnlyList<DocumentBlock> blocks)
        => string.Join("\n\n", blocks.Select(b => b.Text));

    private static string WithBreadcrumb(IReadOnlyList<string> path, string body)
        => path.Count == 0 ? body : BreadcrumbHeader(path) + body;

    private static string BreadcrumbHeader(IReadOnlyList<string> path)
        => path.Count == 0 ? string.Empty : string.Join(" › ", path) + "\n\n";

    private static string Compose(string header, string body)
        => string.IsNullOrEmpty(header) ? body.Trim() : header + body.Trim();

    private static IReadOnlyList<string> TrimPath(IReadOnlyList<string> path, int maxDepth)
        => path.Count <= maxDepth ? path : path.Take(maxDepth).ToList();

    private static bool PathsShareAncestor(IReadOnlyList<string> a, IReadOnlyList<string> b)
    {
        if (a.Count == 0 || b.Count == 0) return true;
        return string.Equals(a[0], b[0], StringComparison.Ordinal);
    }
}

