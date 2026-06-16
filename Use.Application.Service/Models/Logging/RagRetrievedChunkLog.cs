namespace Use.Application.Service.Models.Logging;

/// <summary>
/// One retrieved-chunk record for a single RAG query. Mirrors the
/// <c>rag_retrieved_chunk_log</c> table.
///
/// <para>
/// Both <see cref="DocumentId"/> and <see cref="ChunkId"/> are the internal
/// <c>UUID</c> primary keys of <c>rag_source_documents</c> /
/// <c>rag_document_chunks</c> — <b>not</b> the stable text chunk id
/// (e.g. <c>WikiJs:265:7</c>). The stable text id is resolved to these UUIDs by
/// <c>IChunkReferenceResolver</c> before insert.
/// </para>
/// </summary>
public sealed record RagRetrievedChunkLog(
    Guid Id,
    Guid RagQueryLogId,
    int Rank,
    Guid DocumentId,
    Guid ChunkId,
    double? SemanticScore,
    double? LexicalScore,
    double? RrfScore,
    bool WasSelectedForContext,
    DateTimeOffset CreatedAt);

/// <summary>
/// Input shape for inserting one <c>rag_retrieved_chunk_log</c> row. The UUIDs
/// are already resolved (stable text id → <c>rag_document_chunks.id</c> +
/// <c>rag_source_documents.id</c>).
/// </summary>
public sealed record RagRetrievedChunkLogEntry(
    int Rank,
    Guid DocumentId,
    Guid ChunkId,
    double? SemanticScore,
    double? LexicalScore,
    double? RrfScore,
    bool WasSelectedForContext);

