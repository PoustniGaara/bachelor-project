using Microsoft.Extensions.Options;
using Qdrant.Client;
using Qdrant.Client.Grpc;
using Use.Application.Service.Configuration;
using Use.Application.Service.Models.Retrieval;

namespace Use.Application.Service.Services.VectorSearch;

/// <summary>
/// Qdrant-backed similarity search. Uses the same collection produced by
/// <c>Use.Indexing.Worker</c>, so the payload mapping below mirrors the
/// payload keys written there ("text", "chunkId", "sourceUrl", ...).
/// </summary>
public sealed class QdrantVectorSearchService : IVectorSearchService, IAsyncDisposable
{
    private readonly QdrantClient _client;
    private readonly QdrantOptions _options;
    private readonly ILogger<QdrantVectorSearchService> _logger;

    public QdrantVectorSearchService(
        IOptions<QdrantOptions> options,
        ILogger<QdrantVectorSearchService> logger)
    {
        _options = options.Value;
        _logger = logger;
        _client = new QdrantClient(
            host: _options.Host,
            port: _options.Port,
            https: _options.UseHttps,
            apiKey: _options.ApiKey);
    }

    public async Task<VectorSearchResult> SearchAsync(
        IReadOnlyList<float> queryEmbedding,
        int topK,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(queryEmbedding);
        if (queryEmbedding.Count == 0)
            throw new ArgumentException("Query embedding must not be empty.", nameof(queryEmbedding));

        var limit = (ulong)Math.Max(1, topK);

        IReadOnlyList<ScoredPoint> hits;
        try
        {
            hits = await _client.SearchAsync(
                _options.CollectionName,
                queryEmbedding.ToArray(),
                filter: null,
                limit: limit,
                payloadSelector: true,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Qdrant search failed (collection={Collection}, topK={TopK}).",
                _options.CollectionName, topK);
            throw;
        }

        var chunks = hits.Select(h => MapPayload(h.Payload, h.Score)).ToList();

        _logger.LogDebug("Qdrant returned {Count} chunks from {Collection}.",
            chunks.Count, _options.CollectionName);

        return new VectorSearchResult
        {
            Chunks = chunks,
            CollectionName = _options.CollectionName,
            RequestedTopK = topK
        };
    }

    public async Task<IReadOnlyList<RetrievedChunk>> ListByDocumentAsync(
        string sourceSystem,
        string sourceDocumentId,
        int maxChunks,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sourceDocumentId))
            return Array.Empty<RetrievedChunk>();

        var filter = new Filter
        {
            Must =
            {
                MatchKeyword("sourceDocumentId", sourceDocumentId)
            }
        };
        if (!string.IsNullOrWhiteSpace(sourceSystem))
            filter.Must.Add(MatchKeyword("sourceSystem", sourceSystem));

        var collected = new List<RetrievedChunk>();
        PointId? offset = null;
        const uint pageSize = 256;
        var cap = Math.Max(1, maxChunks);

        try
        {
            while (collected.Count < cap)
            {
                var page = await _client.ScrollAsync(
                    collectionName: _options.CollectionName,
                    filter: filter,
                    limit: pageSize,
                    offset: offset,
                    payloadSelector: true,
                    cancellationToken: cancellationToken).ConfigureAwait(false);

                foreach (var p in page.Result)
                {
                    collected.Add(MapPayload(p.Payload, score: 0f));
                    if (collected.Count >= cap) break;
                }

                if (page.NextPageOffset is null) break;
                offset = page.NextPageOffset;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Qdrant scroll failed (collection={Collection}, sourceSystem={System}, docId={DocId}).",
                _options.CollectionName, sourceSystem, sourceDocumentId);
            throw;
        }

        _logger.LogDebug(
            "Qdrant scroll returned {Count} chunks for {System}:{DocId}.",
            collected.Count, sourceSystem, sourceDocumentId);

        return collected;
    }

    public ValueTask DisposeAsync()
    {
        _client.Dispose();
        return ValueTask.CompletedTask;
    }

    // ---------- payload mapping ----------

    private static RetrievedChunk MapPayload(
        Google.Protobuf.Collections.MapField<string, Value> payload,
        float score)
    {
        var text    = FirstString(payload, "text", "chunkText");
        var title   = FirstString(payload, "title", "pageTitle", "documentTitle");
        var url     = FirstString(payload, "sourceUrl", "url", "path");
        var src     = FirstString(payload, "sourceSystem");
        var docId   = FirstString(payload, "sourceDocumentId", "documentId");
        var chunkId = FirstString(payload, "chunkId");
        var order   = FirstInt(payload, "chunkOrder");

        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in payload)
        {
            var s = ValueToString(value);
            if (s is not null) metadata[key] = s;
        }

        return new RetrievedChunk
        {
            ChunkId = chunkId ?? string.Empty,
            Score = score,
            Text = text ?? string.Empty,
            SourceTitle = title,
            SourceUrl = url,
            SourceSystem = src,
            SourceDocumentId = docId,
            ChunkOrder = order,
            Metadata = metadata
        };
    }

    private static string? FirstString(
        Google.Protobuf.Collections.MapField<string, Value> payload,
        params string[] keys)
    {
        foreach (var key in keys)
        {
            if (payload.TryGetValue(key, out var value))
            {
                var s = ValueToString(value);
                if (!string.IsNullOrEmpty(s)) return s;
            }
        }
        return null;
    }

    private static int? FirstInt(
        Google.Protobuf.Collections.MapField<string, Value> payload,
        params string[] keys)
    {
        foreach (var key in keys)
        {
            if (!payload.TryGetValue(key, out var v)) continue;
            switch (v.KindCase)
            {
                case Value.KindOneofCase.IntegerValue: return (int)v.IntegerValue;
                case Value.KindOneofCase.DoubleValue:  return (int)v.DoubleValue;
                case Value.KindOneofCase.StringValue:
                    if (int.TryParse(v.StringValue, out var parsed)) return parsed;
                    break;
            }
        }
        return null;
    }

    private static string? ValueToString(Value value) => value.KindCase switch
    {
        Value.KindOneofCase.StringValue  => value.StringValue,
        Value.KindOneofCase.IntegerValue => value.IntegerValue.ToString(),
        Value.KindOneofCase.DoubleValue  => value.DoubleValue.ToString("R"),
        Value.KindOneofCase.BoolValue    => value.BoolValue ? "true" : "false",
        Value.KindOneofCase.NullValue    => null,
        _ => null
    };

    private static Condition MatchKeyword(string field, string value) =>
        new() { Field = new FieldCondition { Key = field, Match = new Match { Keyword = value } } };
}

