using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using Use.LlmService.Api.Configuration;
using Use.LlmService.Api.Models;

namespace Use.LlmService.Api.Providers.Chat;

/// <summary>
/// Chat provider that calls a local Ollama instance via HTTP.
/// Streaming is disabled so the controller can return a single JSON response.
/// </summary>
public sealed class OllamaChatProvider : IChatProvider
{
    private readonly HttpClient _httpClient;
    private readonly OllamaOptions _options;
    private readonly ILogger<OllamaChatProvider> _logger;

    public OllamaChatProvider(
        HttpClient httpClient,
        IOptions<OllamaOptions> options,
        ILogger<OllamaChatProvider> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public string Name => "Ollama";

    public async Task<ChatResponse> GenerateAnswerAsync(string prompt, string? systemPrompt, CancellationToken cancellationToken = default)
    {
        var payload = new OllamaGenerateRequest
        {
            Model = _options.ChatModel,
            Prompt = prompt,
            System = systemPrompt,
            Stream = false
        };

        OllamaGenerateResponse? result;
        try
        {
            using var response = await _httpClient.PostAsJsonAsync("api/generate", payload, cancellationToken);
            response.EnsureSuccessStatusCode();
            result = await response.Content.ReadFromJsonAsync<OllamaGenerateResponse>(cancellationToken: cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to reach Ollama at {BaseUrl}", _options.BaseUrl);
            throw new InvalidOperationException("Ollama is unavailable.", ex);
        }

        if (result is null || string.IsNullOrEmpty(result.Response))
        {
            _logger.LogError("Ollama returned an empty generate response.");
            throw new InvalidOperationException("Ollama returned an invalid chat response.");
        }

        return new ChatResponse
        {
            Model = result.Model ?? _options.ChatModel,
            Answer = result.Response
        };
    }
}

