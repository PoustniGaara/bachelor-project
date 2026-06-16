using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using Use.LlmService.Api.Configuration;
using Use.LlmService.Api.Models;

namespace Use.LlmService.Api.Providers.Embeddings;

/// <summary>
/// Embedding provider that calls a local Ollama instance via HTTP.
/// Uses the typed HttpClient registered in Program.cs.
/// </summary>
public sealed class OllamaEmbeddingProvider : IEmbeddingProvider
{
    private readonly HttpClient _httpClient;
    private readonly OllamaOptions _options;
    private readonly ILogger<OllamaEmbeddingProvider> _logger;

    public OllamaEmbeddingProvider(
        HttpClient httpClient,
        IOptions<OllamaOptions> options,
        ILogger<OllamaEmbeddingProvider> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public string Name => "Ollama";

    public async Task<EmbeddingResponse> GenerateEmbeddingAsync(string input, CancellationToken cancellationToken = default)
    {
        var payload = new OllamaEmbedRequest
        {
            Model = _options.EmbeddingModel,
            Input = input
        };

        OllamaEmbedResponse? result;
        try
        {
            using var response = await _httpClient.PostAsJsonAsync("api/embed", payload, cancellationToken);
            response.EnsureSuccessStatusCode();
            result = await response.Content.ReadFromJsonAsync<OllamaEmbedResponse>(cancellationToken: cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to reach Ollama at {BaseUrl}", _options.BaseUrl);
            throw new InvalidOperationException("Ollama is unavailable.", ex);
        }

        if (result?.Embeddings is null || result.Embeddings.Length == 0 || result.Embeddings[0].Length == 0)
        {
            _logger.LogError("Ollama returned an empty embedding response.");
            throw new InvalidOperationException("Ollama returned an invalid embedding response.");
        }

        var vector = result.Embeddings[0];
        return new EmbeddingResponse
        {
            Model = result.Model ?? _options.EmbeddingModel,
            Dimensions = vector.Length,
            Embedding = vector
        };
    }
}

