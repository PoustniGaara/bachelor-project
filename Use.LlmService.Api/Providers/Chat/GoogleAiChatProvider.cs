using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Use.LlmService.Api.Configuration;
using Use.LlmService.Api.Models;

namespace Use.LlmService.Api.Providers.Chat;

/// <summary>
/// Chat provider that calls Google's Generative Language API
/// (https://generativelanguage.googleapis.com/) to access the cloud
/// Gemma models such as "gemma-4-31b-it". Authentication uses the
/// "x-goog-api-key" header. Streaming is disabled so the controller
/// returns a single JSON response.
/// </summary>
public sealed class GoogleAiChatProvider : IChatProvider
{
    private readonly HttpClient _httpClient;
    private readonly GoogleAiOptions _options;
    private readonly ILogger<GoogleAiChatProvider> _logger;

    public GoogleAiChatProvider(
        HttpClient httpClient,
        IOptions<GoogleAiOptions> options,
        ILogger<GoogleAiChatProvider> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public string Name => "GoogleAi";

public async Task<ChatResponse> GenerateAnswerAsync(string prompt, string? systemPrompt, CancellationToken cancellationToken = default)
{
    if (string.IsNullOrWhiteSpace(_options.ApiKey))
    {
        throw new InvalidOperationException(
            "GoogleAi:ApiKey is not configured. Set it via user secrets or environment variables.");
    }

    // Keep the provider-independent prompt abstraction, but map it to
    // Google's native generateContent request shape. System instructions
    // are sent separately from user/RAG content so they are treated as
    // instructions rather than normal text.
    var payload = new GoogleGenerateContentRequest
    {
        SystemInstruction = string.IsNullOrWhiteSpace(systemPrompt)
            ? null
            : new GoogleContent
            {
                Parts = { new GooglePart { Text = systemPrompt } }
            },
        Contents =
        {
            new GoogleContent
            {
                Role = "user",
                Parts = { new GooglePart { Text = prompt } }
            }
        },
        GenerationConfig = new GoogleGenerationConfig
        {
            Temperature = _options.Temperature,
            MaxOutputTokens = _options.MaxOutputTokens,
            ThinkingConfig = null // ThinkingConfig is optional and can be omitted if not needed
        }
    };

    _logger.LogDebug(
        "Calling Google AI model {Model}. SystemInstructionPresent={SystemInstructionPresent}",
        _options.ChatModel,
        !string.IsNullOrWhiteSpace(systemPrompt));

    var requestUri = $"models/{_options.ChatModel}:generateContent";

    GoogleGenerateContentResponse? result;
    try
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, requestUri)
        {
            Content = JsonContent.Create(payload)
        };
        request.Headers.Add("x-goog-api-key", _options.ApiKey);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError(
                "Google AI returned {Status} for model {Model}: {Body}",
                (int)response.StatusCode, _options.ChatModel, body);
            response.EnsureSuccessStatusCode();
        }

        var rawJson = await response.Content.ReadAsStringAsync(cancellationToken);

        _logger.LogInformation("Raw Google AI response: {RawJson}", rawJson);

        result = JsonSerializer.Deserialize<GoogleGenerateContentResponse>(rawJson);
    }
    catch (HttpRequestException ex)
    {
        _logger.LogError(ex, "Failed to reach Google AI at {BaseUrl}", _options.BaseUrl);
        throw new InvalidOperationException("Google AI is unavailable.", ex);
    }

    var parts = result?.Candidates?
        .FirstOrDefault()?.Content?.Parts;

    var answer = parts is null
        ? null
        : string.Join(
            "\n",
            parts
                .Where(p => p.Thought != true)
                .Select(p => p.Text)
                .Where(t => !string.IsNullOrWhiteSpace(t)));

    if (string.IsNullOrEmpty(answer))
    {
        _logger.LogError("Google AI returned an empty generateContent response.");
        throw new InvalidOperationException("Google AI returned an invalid chat response.");
    }

    return new ChatResponse
    {
        Model = result?.ModelVersion ?? _options.ChatModel,
        Answer = answer.Trim()
    };
}
}

