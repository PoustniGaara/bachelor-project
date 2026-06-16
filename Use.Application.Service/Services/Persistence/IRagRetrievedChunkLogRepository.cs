using Use.Application.Service.Models.Logging;

namespace Use.Application.Service.Services.Persistence;

/// <summary>
/// Data access for the <c>rag_retrieved_chunk_log</c> table. Used for offline
/// debugging of bad answers ("what did retrieval actually surface?").
///
/// <para>
/// No deletion / retention policy is implemented yet (rows accumulate; pruning
/// is a future task — see README TODOs).
/// </para>
/// </summary>
public interface IRagRetrievedChunkLogRepository
{
    /// <summary>
    /// Insert the retrieved-chunk rows for one RAG query in a single transaction.
    /// Returns the number of rows inserted.
    /// </summary>
    Task<int> InsertManyAsync(
        Guid ragQueryLogId, IReadOnlyList<RagRetrievedChunkLogEntry> entries, CancellationToken cancellationToken);
}

