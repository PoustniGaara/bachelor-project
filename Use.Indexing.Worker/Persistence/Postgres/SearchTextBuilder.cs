using System.Text;
using Use.Indexing.Worker.Models;

namespace Use.Indexing.Worker.Persistence.Postgres;

/// <summary>
/// Builds the enriched lexical text persisted into <c>rag_document_chunks.search_text</c>
/// (and indexed by the generated <c>search_vector</c> tsvector). It mirrors the
/// shape of <see cref="Use.Indexing.Worker.Embeddings.EmbeddingTextBuilder"/> —
/// prepending source/title/path/description/heading context above the clean
/// chunk content — so BM25-style queries can match terms that live in metadata
/// (e.g. a document title or path) and not only in the body text.
///
/// <para>
/// Unlike the embedding text, this output deliberately contains no
/// embedding-specific metadata (model, dimensions). It is purely lexical and
/// does not depend on embeddings having been produced.
/// </para>
/// </summary>
public interface ISearchTextBuilder
{
    /// <summary>Builds the enriched, BM25-friendly lexical text for a chunk.</summary>
    string Build(DocumentChunk chunk);
}

/// <inheritdoc />
public sealed class SearchTextBuilder : ISearchTextBuilder
{
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

    private static void AppendField(StringBuilder sb, string label, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        sb.Append(label).Append(": ").AppendLine(value.Trim());
    }

    private static string ResolveTitle(DocumentChunk chunk)
        => chunk.Metadata.TryGetValue("title", out var t) && !string.IsNullOrWhiteSpace(t)
            ? t
            : chunk.Reference.Title;

    private static string? Get(DocumentChunk chunk, string key)
        => chunk.Metadata.TryGetValue(key, out var v) ? v : null;
}

