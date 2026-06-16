using Use.Application.Service.Models.Retrieval;

namespace Use.Application.Service.Services.LexicalSearch;

/// <summary>
/// Lexical (keyword / full-text) retrieval over the BM25-ready chunk store
/// written by <c>Use.Indexing.Worker</c> (table <c>rag_document_chunks</c>).
/// The default implementation uses PostgreSQL full-text search; it can later be
/// swapped for a true BM25 ranker without touching the orchestrator.
/// </summary>
public interface ILexicalSearchService
{
    /// <summary>
    /// True when a PostgreSQL connection is configured and lexical search can run.
    /// When false the orchestrator degrades to semantic-only retrieval.
    /// </summary>
    bool Enabled { get; }

    /// <summary>
    /// Runs a lexical search for <paramref name="query"/> and returns up to
    /// <paramref name="limit"/> chunks ordered by lexical relevance.
    /// </summary>
    Task<IReadOnlyList<LexicalSearchResult>> SearchAsync(
        string query,
        int limit,
        string? sourceSystem,
        CancellationToken cancellationToken);
}

