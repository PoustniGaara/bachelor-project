using System.Text.Json.Serialization;

namespace Use.LlmService.Api.Models;

/// <summary>
/// Wire-format response for Google's Generative Language API
/// (POST /v1beta/models/{model}:generateContent).
/// </summary>
public sealed class GoogleGenerateContentResponse
{
    [JsonPropertyName("candidates")]
    public List<GoogleCandidate>? Candidates { get; set; }

    [JsonPropertyName("modelVersion")]
    public string? ModelVersion { get; set; }
}

public sealed class GoogleCandidate
{
    [JsonPropertyName("content")]
    public GoogleContent? Content { get; set; }

    [JsonPropertyName("finishReason")]
    public string? FinishReason { get; set; }
}

