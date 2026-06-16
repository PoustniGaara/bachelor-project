using System.Text;
using Use.Indexing.Worker.Models;

namespace Use.Indexing.Worker.Embeddings;

/// <summary>
/// Builds the enriched text that is fed to the embedding model. The enriched
/// text prepends a small, deterministic header describing the chunk's source
/// (system, document title, path, description) and structural location
/// (heading path, chunk order) above the clean chunk content.
///
/// <para>
/// The builder must be deterministic: the same chunk always produces the
/// same enriched string. This keeps embedding hashes stable and makes the
/// dump output reproducible.
/// </para>
/// </summary>
public interface IEmbeddingTextBuilder
{
    /// <summary>Returns a copy of <paramref name="chunks"/> with <see cref="DocumentChunk.EmbeddingText"/> populated.</summary>
    IReadOnlyList<DocumentChunk> Enrich(IReadOnlyList<DocumentChunk> chunks);

    /// <summary>Builds the enriched embedding text for a single chunk.</summary>
    string Build(DocumentChunk chunk);
}

/// <inheritdoc />
public sealed class EmbeddingTextBuilder : IEmbeddingTextBuilder
{
    public IReadOnlyList<DocumentChunk> Enrich(IReadOnlyList<DocumentChunk> chunks)
    {
        if (chunks.Count == 0) return chunks;

        var enriched = new DocumentChunk[chunks.Count];
        for (var i = 0; i < chunks.Count; i++)
        {
            var c = chunks[i];
            enriched[i] = c with { EmbeddingText = Build(c) };
        }
        return enriched;
    }

    public string Build(DocumentChunk chunk)
    {
        ArgumentNullException.ThrowIfNull(chunk);

        var sb = new StringBuilder(capacity: chunk.Text.Length + 256);

        AppendField(sb, "Source system",       chunk.Reference.SourceSystem.ToString());
        AppendField(sb, "Document title",      ResolveTitle(chunk));
        AppendField(sb, "Document path",       Get(chunk, "path"));
        AppendField(sb, "Document description", Get(chunk, "description"));
        AppendField(sb, "Heading path",        Get(chunk, "headingPath"));
        AppendField(sb, "Chunk order",         chunk.Order.ToString());

        sb.AppendLine();
        sb.AppendLine("Content:");
        sb.Append(chunk.Text);

        return sb.ToString();
    }

    // ---------- helpers ----------

    private static void AppendField(StringBuilder sb, string label, string? value)
    {
        // Skip noisy empty fields; embeddings are sensitive to filler tokens.
        if (string.IsNullOrWhiteSpace(value)) return;
        sb.Append(label).Append(": ").AppendLine(value.Trim());
    }

    private static string ResolveTitle(DocumentChunk chunk)
    {
        if (chunk.Metadata.TryGetValue("title", out var t) && !string.IsNullOrWhiteSpace(t))
            return t;
        return chunk.Reference.Title;
    }

    private static string? Get(DocumentChunk chunk, string key)
        => chunk.Metadata.TryGetValue(key, out var v) ? v : null;
}

