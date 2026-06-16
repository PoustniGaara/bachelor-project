using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using Use.Application.Service.Common;
using Use.Application.Service.Configuration;

namespace Use.Application.Service.Services.Embeddings;

/// <summary>
/// HTTP client for the Use.LlmService API. Configured via
/// <see cref="LlmServiceOptions"/> and a typed <see cref="HttpClient"/>
/// supplied by <c>IHttpClientFactory</c>.
/// </summary>
public sealed class LlmServiceClient : ILlmServiceClient
{
    private readonly HttpClient _http;
    private readonly LlmServiceOptions _options;
    private readonly ILogger<LlmServiceClient> _logger;

    public LlmServiceClient(
        HttpClient http,
        IOptions<LlmServiceOptions> options,
        ILogger<LlmServiceClient> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<EmbeddingResponse> CreateEmbeddingAsync(
        EmbeddingRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var endpoint = _options.EmbeddingEndpoint;
        _logger.LogDebug("Calling LLM embedding endpoint {Endpoint}.", endpoint);

        using var response = await _http
            .PostAsJsonAsync(endpoint, request, cancellationToken)
            .ConfigureAwait(false);

        await EnsureSuccessAsync(response, "embedding", cancellationToken).ConfigureAwait(false);

        var payload = await response.Content
            .ReadFromJsonAsync<EmbeddingResponse>(cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (payload is null || payload.Embedding.Length == 0)
            throw new InvalidOperationException("LLM service returned an empty embedding payload.");

        return payload;
    }

    public async Task<GenerationResponse> GenerateAsync(
        GenerationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var endpoint = _options.GenerationEndpoint;
        _logger.LogDebug("Calling LLM generation endpoint {Endpoint}.", endpoint);

        using var response = await _http
            .PostAsJsonAsync(endpoint, request, cancellationToken)
            .ConfigureAwait(false);

        await EnsureSuccessAsync(response, "generation", cancellationToken).ConfigureAwait(false);

        var payload = await response.Content
            .ReadFromJsonAsync<GenerationResponse>(cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (payload is null)
            throw new InvalidOperationException("LLM service returned an empty generation payload.");

        return payload;
    }

    public async Task<RerankResponse> RerankAsync(
        string query,
        IReadOnlyList<RerankDocument> documents,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        ArgumentNullException.ThrowIfNull(documents);
        if (documents.Count == 0)
            throw new ArgumentException("Documents must not be empty.", nameof(documents));

        var endpoint = _options.RerankEndpoint;
        _logger.LogDebug(
            "Calling LLM rerank endpoint {Endpoint} with {Count} documents.", endpoint, documents.Count);

        var request = new RerankRequest
        {
            Query = query,
            Documents = documents.ToList()
        };

        using var response = await _http
            .PostAsJsonAsync(endpoint, request, cancellationToken)
            .ConfigureAwait(false);

        await EnsureSuccessAsync(response, "rerank", cancellationToken).ConfigureAwait(false);

        var payload = await response.Content
            .ReadFromJsonAsync<RerankResponse>(cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (payload?.Results is null)
            throw new InvalidOperationException("LLM service returned an empty rerank payload.");

        return payload;
    }

    private async Task EnsureSuccessAsync(
        HttpResponseMessage response, string operation, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode) return;

        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        _logger.LogError(
            "LLM service {Operation} call failed: {StatusCode}. Body length={Length}.",
            operation, (int)response.StatusCode, body.Length);

        throw new InvalidOperationException(
            $"LLM service {operation} call failed with status {(int)response.StatusCode}.");
    }
}

