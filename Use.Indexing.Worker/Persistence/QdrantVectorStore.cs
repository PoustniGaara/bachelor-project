using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Qdrant.Client;
using Qdrant.Client.Grpc;
using Use.Indexing.Worker.Configuration;
using Use.Indexing.Worker.Models;

namespace Use.Indexing.Worker.Persistence;

/// <summary>
/// Qdrant-backed <see cref="IVectorStore"/>. On first use it lazily ensures the
/// configured collection exists with the correct vector size and distance
/// metric. Each chunk's string id is hashed into a deterministic Guid so the
/// same chunk always maps to the same Qdrant point id, making upserts
/// idempotent and enabling targeted deletes by document.
/// </summary>
public sealed class QdrantVectorStore : IVectorStore, IAsyncDisposable
{
    private readonly QdrantClient _client;
    private readonly QdrantOptions _options;
    private readonly ILogger<QdrantVectorStore> _logger;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private bool _initialized;

    public QdrantVectorStore(IOptions<IndexingOptions> options, ILogger<QdrantVectorStore> logger)
    {
        _options = options.Value.Qdrant;
        _logger = logger;
        _client = new QdrantClient(
            host: _options.Host,
            port: _options.Port,
            https: _options.UseHttps,
            apiKey: _options.ApiKey);
    }

    public async Task UpsertAsync(IReadOnlyList<EmbeddedChunk> embedded, CancellationToken cancellationToken)
    {
        if (embedded.Count == 0) return;

        await EnsureCollectionAsync(embedded[0].Dimensions, cancellationToken).ConfigureAwait(false);

        var points = embedded.Select(BuildPoint).ToList();

        await _client.UpsertAsync(_options.CollectionName, points, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        _logger.LogDebug("Upserted {Count} vectors into Qdrant collection {Collection}.",
            points.Count, _options.CollectionName);
    }

    public async Task DeleteByDocumentAsync(SourceDocumentReference reference, CancellationToken cancellationToken)
    {
        if (!_initialized && !await CollectionExistsAsync(cancellationToken).ConfigureAwait(false))
            return;

        var filter = new Filter
        {
            Must =
            {
                MatchKeyword("sourceSystem", reference.SourceSystem.ToString()),
                MatchKeyword("sourceDocumentId", reference.SourceDocumentId)
            }
        };

        await _client.DeleteAsync(_options.CollectionName, filter, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        _logger.LogDebug("Deleted vectors for {Source}:{DocId} from Qdrant.",
            reference.SourceSystem, reference.SourceDocumentId);
    }

    public ValueTask DisposeAsync()
    {
        _client.Dispose();
        _initLock.Dispose();
        return ValueTask.CompletedTask;
    }

    // ---------- private ----------

    private async Task EnsureCollectionAsync(int actualDimensions, CancellationToken ct)
    {
        if (_initialized) return;

        await _initLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_initialized) return;

            var configuredSize = (ulong)Math.Max(_options.VectorSize, 1);
            if (actualDimensions != _options.VectorSize)
            {
                _logger.LogWarning(
                    "Configured Qdrant VectorSize ({Configured}) differs from embedding dimensions ({Actual}); using actual.",
                    _options.VectorSize, actualDimensions);
                configuredSize = (ulong)actualDimensions;
            }

            if (await CollectionExistsAsync(ct).ConfigureAwait(false))
            {
                _initialized = true;
                return;
            }

            _logger.LogInformation(
                "Creating Qdrant collection {Collection} (size={Size}, distance={Distance}).",
                _options.CollectionName, configuredSize, _options.Distance);

            await _client.CreateCollectionAsync(
                _options.CollectionName,
                new VectorParams
                {
                    Size = configuredSize,
                    Distance = ParseDistance(_options.Distance)
                },
                cancellationToken: ct).ConfigureAwait(false);

            _initialized = true;
        }
        finally
        {
            _initLock.Release();
        }
    }

    private async Task<bool> CollectionExistsAsync(CancellationToken ct)
    {
        try
        {
            return await _client.CollectionExistsAsync(_options.CollectionName, ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check Qdrant collection existence.");
            throw;
        }
    }

    private static PointStruct BuildPoint(EmbeddedChunk e)
    {
        var point = new PointStruct
        {
            Id = new PointId { Uuid = DeterministicUuid(e.Chunk.ChunkId).ToString() },
            Vectors = e.Embedding.ToArray()
        };

        // Payload is what the retrieval service uses to render and filter
        // results. We store the clean original chunk text (used as RAG
        // context), the enriched embedding text (debug only), and the
        // metadata fields the retrieval and prompt-building stages need.
        var payload = point.Payload;
        payload["chunkId"]          = e.Chunk.ChunkId;
        payload["sourceSystem"]     = e.Chunk.Reference.SourceSystem.ToString();
        payload["sourceDocumentId"] = e.Chunk.Reference.SourceDocumentId;
        payload["sourceUrl"]        = e.Chunk.Reference.Url;
        payload["title"]            = MetadataOrDefault(e.Chunk, "title", e.Chunk.Reference.Title);
        payload["path"]             = MetadataOrDefault(e.Chunk, "path", string.Empty);
        payload["description"]      = MetadataOrDefault(e.Chunk, "description", string.Empty);
        payload["headingPath"]      = MetadataOrDefault(e.Chunk, "headingPath", string.Empty);
        payload["chunkOrder"]       = e.Chunk.Order;
        payload["text"]             = e.Chunk.Text; // clean original — used by RAG
        if (!string.IsNullOrEmpty(e.Chunk.EmbeddingText))
            payload["embeddingText"] = e.Chunk.EmbeddingText; // diagnostics
        payload["model"]            = e.Model;
        payload["dimensions"]       = e.Dimensions;

        foreach (var kv in e.Chunk.Metadata)
        {
            // Don't overwrite the structured keys above with the string copies
            // the chunker also placed in metadata.
            if (!payload.ContainsKey(kv.Key))
                payload[kv.Key] = kv.Value ?? string.Empty;
        }

        return point;
    }

    private static string MetadataOrDefault(DocumentChunk chunk, string key, string fallback)
        => chunk.Metadata.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v : fallback;

    /// <summary>
    /// Hashes a string id into a stable Guid (UUID-style). Qdrant requires
    /// point ids to be either UInt64 or UUID; our chunk ids look like
    /// "WikiJs:265:0" which is neither. Using SHA-256 and taking the first
    /// 16 bytes gives us a deterministic, collision-resistant mapping.
    /// </summary>
    private static Guid DeterministicUuid(string id)
    {
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(Encoding.UTF8.GetBytes(id), hash);
        return new Guid(hash[..16]);
    }

    private static Distance ParseDistance(string value) => value?.Trim().ToLowerInvariant() switch
    {
        "cosine" => Distance.Cosine,
        "dot"    => Distance.Dot,
        "euclid" or "euclidean" => Distance.Euclid,
        "manhattan" => Distance.Manhattan,
        _ => Distance.Cosine
    };

    private static Condition MatchKeyword(string field, string value) =>
        new() { Field = new FieldCondition { Key = field, Match = new Match { Keyword = value } } };
}

