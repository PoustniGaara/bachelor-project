using System.Collections.Concurrent;
using Use.Indexing.Worker.Models;

namespace Use.Indexing.Worker.Persistence;

/// <summary>
/// In-memory stub repository. Lets the worker run end-to-end without a DB.
/// Replace with EF Core / Dapper-backed repository in production.
/// </summary>
public sealed class InMemoryIndexRepository : IIndexRepository
{
    private readonly ConcurrentDictionary<string, NormalizedDocument> _docs = new();
    private readonly ConcurrentDictionary<string, IReadOnlyList<DocumentChunk>> _chunks = new();
    private readonly ConcurrentDictionary<SourceSystemType, DateTimeOffset> _lastIndexed = new();
    private readonly ILogger<InMemoryIndexRepository> _logger;

    public InMemoryIndexRepository(ILogger<InMemoryIndexRepository> logger) => _logger = logger;

    public Task<DateTimeOffset?> GetLastIndexedAtAsync(SourceSystemType source, CancellationToken ct)
        => Task.FromResult(_lastIndexed.TryGetValue(source, out var t) ? t : (DateTimeOffset?)null);

    public Task UpsertDocumentAsync(NormalizedDocument document, CancellationToken ct)
    {
        _docs[Key(document.Reference)] = document;
        _logger.LogDebug("Upserted document {Key}", Key(document.Reference));
        return Task.CompletedTask;
    }

    public Task ReplaceChunksAsync(SourceDocumentReference reference, IReadOnlyList<DocumentChunk> chunks, CancellationToken ct)
    {
        _chunks[Key(reference)] = chunks;
        return Task.CompletedTask;
    }

    public Task SetLastIndexedAtAsync(SourceSystemType source, DateTimeOffset timestamp, CancellationToken ct)
    {
        _lastIndexed[source] = timestamp;
        return Task.CompletedTask;
    }

    private static string Key(SourceDocumentReference r) => $"{r.SourceSystem}:{r.SourceDocumentId}";
}

/// <summary>In-memory stub vector store, by chunk id.</summary>
public sealed class InMemoryVectorStore : IVectorStore
{
    private readonly ConcurrentDictionary<string, EmbeddedChunk> _vectors = new();
    private readonly ILogger<InMemoryVectorStore> _logger;

    public InMemoryVectorStore(ILogger<InMemoryVectorStore> logger) => _logger = logger;

    public Task UpsertAsync(IReadOnlyList<EmbeddedChunk> embedded, CancellationToken ct)
    {
        foreach (var e in embedded) _vectors[e.Chunk.ChunkId] = e;
        _logger.LogDebug("Upserted {Count} vectors (total stored: {Total})", embedded.Count, _vectors.Count);
        return Task.CompletedTask;
    }

    public Task DeleteByDocumentAsync(SourceDocumentReference reference, CancellationToken ct)
    {
        var prefix = $"{reference.SourceSystem}:{reference.SourceDocumentId}:";
        foreach (var k in _vectors.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).ToList())
            _vectors.TryRemove(k, out _);
        return Task.CompletedTask;
    }
}

