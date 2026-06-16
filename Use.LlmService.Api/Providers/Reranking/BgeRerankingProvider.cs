using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using Use.LlmService.Api.Configuration;
using Use.LlmService.Api.Models;

namespace Use.LlmService.Api.Providers.Reranking;

/// <summary>
/// Reranking provider that calls the external Python FastAPI service hosting
/// <c>BAAI/bge-reranker-v2-m3</c> via FlagEmbedding. Uses the typed HttpClient
/// registered in Program.cs.
///
/// <para>
/// The Python service's request/response contract is intentionally identical to
/// the gateway's provider-agnostic DTOs, so payloads are forwarded verbatim.
/// </para>
/// </summary>
public sealed class BgeRerankingProvider : IRerankingProvider
{
    private readonly HttpClient _httpClient;
    private readonly RerankerOptions _options;
    private readonly ILogger<BgeRerankingProvider> _logger;

    public BgeRerankingProvider(
        HttpClient httpClient,
        IOptions<RerankerOptions> options,
        ILogger<BgeRerankingProvider> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public string Name => "Bge";

    public async Task<RerankResponse> RerankAsync(
        string query,
        IReadOnlyList<RerankDocument> documents,
        CancellationToken cancellationToken = default)
    {
        var payload = new RerankRequest
        {
            Query = query,
            Documents = documents.ToList()
        };

        var endpoint = _options.Endpoint.TrimStart('/');

        RerankResponse? result;
        try
        {
            using var response = await _httpClient
                .PostAsJsonAsync(endpoint, payload, cancellationToken)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            result = await response.Content
                .ReadFromJsonAsync<RerankResponse>(cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to reach the reranker service at {BaseUrl}", _options.BaseUrl);
            throw new InvalidOperationException("Reranker service is unavailable.", ex);
        }

        if (result?.Results is null)
        {
            _logger.LogError("Reranker service returned an empty/invalid response.");
            throw new InvalidOperationException("Reranker service returned an invalid response.");
        }

        // The Python service already sorts by score descending; re-sort defensively
        // so the contract holds regardless of backend behaviour.
        result.Results = result.Results
            .OrderByDescending(r => r.Score)
            .ToList();

        if (string.IsNullOrWhiteSpace(result.Model))
            result.Model = _options.Model;

        return result;
    }
}

