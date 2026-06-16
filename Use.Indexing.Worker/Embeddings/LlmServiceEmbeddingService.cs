using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using Use.Indexing.Worker.Configuration;
using Use.Indexing.Worker.Models;

namespace Use.Indexing.Worker.Embeddings;

/// <summary>
/// <see cref="IEmbeddingService"/> implementation that delegates to the
/// Use.LlmService.Api (POST /api/embeddings). The remote service in turn
/// calls a local Ollama model (e.g. <c>embeddinggemma</c>).
///
/// The LLM API exposes a per-text endpoint, so this service issues one
/// HTTP call per chunk while bounding concurrency with a SemaphoreSlim to
/// avoid overwhelming the single-threaded local model runtime.
/// </summary>
public sealed class LlmServiceEmbeddingService : IEmbeddingService
{
    public const string HttpClientName = "LlmService";

    private readonly IHttpClientFactory _httpFactory;
    private readonly LlmServiceOptions _options;
    private readonly EmbeddingOptions _embeddingOptions;
    private readonly ILogger<LlmServiceEmbeddingService> _logger;

    public LlmServiceEmbeddingService(
        IHttpClientFactory httpFactory,
        IOptions<IndexingOptions> options,
        ILogger<LlmServiceEmbeddingService> logger)
    {
        _httpFactory = httpFactory;
        _options = options.Value.LlmService;
        _embeddingOptions = options.Value.Embedding;
        _logger = logger;
    }

    public async Task<IReadOnlyList<EmbeddedChunk>> EmbedAsync(
        IReadOnlyList<DocumentChunk> chunks, CancellationToken cancellationToken)
    {
        if (chunks.Count == 0) return Array.Empty<EmbeddedChunk>();

        var concurrency = Math.Max(1, _options.MaxParallelism);
        using var gate = new SemaphoreSlim(concurrency, concurrency);

        var tasks = chunks.Select(async chunk =>
        {
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                return await EmbedSingleAsync(chunk, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                gate.Release();
            }
        });

        var results = await Task.WhenAll(tasks).ConfigureAwait(false);
        return results;
    }

    private async Task<EmbeddedChunk> EmbedSingleAsync(DocumentChunk chunk, CancellationToken ct)
    {
        // Embed the enriched text when available; fall back to the clean text.
        // The clean text remains untouched on the chunk and is what gets stored
        // for display / RAG context.
        var input = string.IsNullOrEmpty(chunk.EmbeddingText) ? chunk.Text : chunk.EmbeddingText;
        var payload = new EmbeddingRequestDto(input, "DocumentChunk");
        var attempt = 0;

        while (true)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var http = _httpFactory.CreateClient(HttpClientName);

                using var response = await http.PostAsJsonAsync(
                    "api/embeddings", payload, ct).ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                    throw new HttpRequestException(
                        $"LLM service returned {(int)response.StatusCode}: {body}");
                }

                var dto = await response.Content
                    .ReadFromJsonAsync<EmbeddingResponseDto>(cancellationToken: ct)
                    .ConfigureAwait(false)
                    ?? throw new InvalidOperationException("LLM service returned an empty body.");

                if (dto.Embedding is null || dto.Embedding.Length == 0)
                    throw new InvalidOperationException("LLM service returned an empty embedding.");

                var model = string.IsNullOrWhiteSpace(dto.Model) ? _embeddingOptions.Model : dto.Model;
                var dims = dto.Dimensions > 0 ? dto.Dimensions : dto.Embedding.Length;

                return new EmbeddedChunk(chunk, dto.Embedding, model, dims);
            }
            catch (Exception ex) when (IsTransient(ex) && attempt < _options.MaxRetries)
            {
                attempt++;
                var delay = TimeSpan.FromMilliseconds(200 * Math.Pow(2, attempt));
                _logger.LogWarning(ex,
                    "Embedding call failed (attempt {Attempt}/{Max}); retrying in {Delay}.",
                    attempt, _options.MaxRetries, delay);
                await Task.Delay(delay, ct).ConfigureAwait(false);
            }
        }
    }

    private static bool IsTransient(Exception ex) =>
        ex is HttpRequestException or TaskCanceledException or IOException;

    // Wire-format DTOs — kept private so the worker stays decoupled from the
    // API project's assemblies. Property names match the LLM API JSON schema.
    private sealed record EmbeddingRequestDto(string Input, string? SourceType);

    private sealed class EmbeddingResponseDto
    {
        public string Model { get; set; } = string.Empty;
        public int Dimensions { get; set; }
        public float[] Embedding { get; set; } = Array.Empty<float>();
    }
}

