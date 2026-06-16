using System.Text;
using Microsoft.Extensions.Options;
using Use.Indexing.Worker.Configuration;
using Use.Indexing.Worker.Models;

namespace Use.Indexing.Worker.Diagnostics;

/// <summary>
/// Writes the normalized document text and its chunks to disk for inspection.
/// This represents the exact text that will be sent to the embedding model,
/// making it easy to verify chunk boundaries, breadcrumb headers, and the
/// markdown→plain-text conversion. Disabled in production via configuration.
/// </summary>
public interface IChunkDumpWriter
{
    bool IsEnabled { get; }

    /// <summary>Resets the per-source output folder. Call once per cycle.</summary>
    void BeginCycle(SourceSystemType source);

    /// <summary>Writes one document's normalized text and all chunks.</summary>
    Task WriteAsync(NormalizedDocument document, IReadOnlyList<DocumentChunk> chunks, CancellationToken ct);
}

/// <inheritdoc />
public sealed class ChunkDumpWriter : IChunkDumpWriter
{
    // Reserved Windows + cross-platform-unfriendly characters, plus path separators.
    private static readonly char[] InvalidPathChars =
        Path.GetInvalidFileNameChars().Concat(new[] { '/', '\\', ':', '?', '*', '"', '<', '>', '|' })
            .Distinct().ToArray();

    private readonly ChunkDumpOptions _options;
    private readonly ILogger<ChunkDumpWriter> _logger;
    private readonly HashSet<SourceSystemType> _cleanedSources = new();
    private readonly object _gate = new();

    public ChunkDumpWriter(IOptions<IndexingOptions> options, ILogger<ChunkDumpWriter> logger)
    {
        _options = options.Value.ChunkDump;
        _logger = logger;
    }

    public bool IsEnabled => _options.Enabled;

    public void BeginCycle(SourceSystemType source)
    {
        if (!_options.Enabled) return;
        lock (_gate) _cleanedSources.Remove(source);
    }

    public async Task WriteAsync(NormalizedDocument document, IReadOnlyList<DocumentChunk> chunks, CancellationToken ct)
    {
        if (!_options.Enabled) return;

        try
        {
            var sourceDir = EnsureSourceDirectory(document.Reference.SourceSystem);
            var docDir = EnsureDocumentDirectory(sourceDir, document.Reference);

            if (_options.IncludeFullDocument)
                await WriteFullDocumentAsync(docDir, document, chunks.Count, ct).ConfigureAwait(false);

            var width = Math.Max(2, chunks.Count.ToString().Length);
            for (var i = 0; i < chunks.Count; i++)
                await WriteChunkAsync(docDir, chunks[i], i, chunks.Count, width, ct).ConfigureAwait(false);

            _logger.LogDebug("Dumped {Count} chunks for {Source}:{DocId} → {Dir}",
                chunks.Count, document.Reference.SourceSystem, document.Reference.SourceDocumentId, docDir);
        }
        catch (Exception ex)
        {
            // Diagnostics must never break the indexing pipeline.
            _logger.LogWarning(ex, "Failed to dump chunks for {Source}:{DocId}",
                document.Reference.SourceSystem, document.Reference.SourceDocumentId);
        }
    }

    // ---------- private ----------

    private string EnsureSourceDirectory(SourceSystemType source)
    {
        var root = Path.IsPathRooted(_options.OutputDirectory)
            ? _options.OutputDirectory
            : Path.Combine(AppContext.BaseDirectory, _options.OutputDirectory);

        var sourceDir = Path.Combine(root, source.ToString());

        bool shouldClean;
        lock (_gate)
        {
            shouldClean = _options.CleanOnStart && _cleanedSources.Add(source);
        }

        if (shouldClean && Directory.Exists(sourceDir))
        {
            try { Directory.Delete(sourceDir, recursive: true); }
            catch (IOException ex) { _logger.LogWarning(ex, "Could not clean dump folder {Dir}", sourceDir); }
        }

        Directory.CreateDirectory(sourceDir);
        return sourceDir;
    }

    private static string EnsureDocumentDirectory(string sourceDir, SourceDocumentReference reference)
    {
        // Folder name: "<sanitized-title>__<sanitized-id>" — readable + unique.
        var name = $"{Sanitize(reference.Title, maxLength: 80)}__{Sanitize(reference.SourceDocumentId, maxLength: 40)}";
        var dir = Path.Combine(sourceDir, name);
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static async Task WriteFullDocumentAsync(
        string docDir, NormalizedDocument doc, int chunkCount, CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Normalized document");
        sb.Append("Source:        ").AppendLine(doc.Reference.SourceSystem.ToString());
        sb.Append("Document id:   ").AppendLine(doc.Reference.SourceDocumentId);
        sb.Append("Title:         ").AppendLine(doc.Title);
        sb.Append("URL:           ").AppendLine(doc.Reference.Url);
        if (doc.Reference.LastModified is { } lm)
            sb.Append("Last modified: ").AppendLine(lm.ToString("u"));
        sb.Append("Chunks:        ").AppendLine(chunkCount.ToString());
        if (doc.Tags.Count > 0)
            sb.Append("Tags:          ").AppendLine(string.Join(", ", doc.Tags));
        sb.AppendLine(new string('-', 72));
        sb.AppendLine();
        sb.AppendLine(doc.PlainText);

        await File.WriteAllTextAsync(
            Path.Combine(docDir, "_full.txt"), sb.ToString(), Encoding.UTF8, ct).ConfigureAwait(false);
    }

    private static async Task WriteChunkAsync(
        string docDir, DocumentChunk chunk, int index, int total, int width, CancellationToken ct)
    {
        var headingPath = chunk.Metadata.TryGetValue("headingPath", out var hp) ? hp : string.Empty;
        var headingSlug = string.IsNullOrWhiteSpace(headingPath)
            ? string.Empty
            : "_" + Sanitize(headingPath.Split('›').Last().Trim(), maxLength: 40);

        var fileName = $"chunk_{(index + 1).ToString().PadLeft(width, '0')}_of_{total}{headingSlug}.txt";
        var path = Path.Combine(docDir, fileName);

        var sb = new StringBuilder();
        sb.Append("Chunk:        ").Append(index + 1).Append(" / ").AppendLine(total.ToString());
        sb.Append("Chunk id:     ").AppendLine(chunk.ChunkId);
        sb.Append("Order:        ").AppendLine(chunk.Order.ToString());
        sb.Append("Characters:   ").AppendLine(chunk.Text.Length.ToString());
        foreach (var kv in chunk.Metadata.OrderBy(k => k.Key, StringComparer.Ordinal))
            sb.Append(kv.Key.PadRight(13)).Append(' ').AppendLine(kv.Value);
        sb.AppendLine(new string('-', 72));

        if (!string.IsNullOrEmpty(chunk.EmbeddingText))
        {
            sb.AppendLine();
            sb.AppendLine("=== Embedding input (sent to model) ===");
            sb.AppendLine(chunk.EmbeddingText);
            sb.AppendLine(new string('-', 72));
        }

        sb.AppendLine();
        sb.AppendLine("=== Original chunk text (used for RAG context) ===");
        sb.AppendLine(chunk.Text);

        await File.WriteAllTextAsync(path, sb.ToString(), Encoding.UTF8, ct).ConfigureAwait(false);
    }

    private static string Sanitize(string value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return "untitled";

        var sb = new StringBuilder(value.Length);
        foreach (var c in value.Trim())
        {
            if (Array.IndexOf(InvalidPathChars, c) >= 0 || char.IsControl(c))
                sb.Append('_');
            else if (char.IsWhiteSpace(c))
                sb.Append(' ');
            else
                sb.Append(c);
        }

        var cleaned = sb.ToString().Trim().TrimEnd('.', ' ');
        if (cleaned.Length == 0) cleaned = "untitled";
        return cleaned.Length <= maxLength ? cleaned : cleaned[..maxLength].TrimEnd();
    }
}

