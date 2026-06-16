using System.Text.Json.Serialization;

namespace Use.LlmService.Api.Models;

/// <summary>
/// Wire-format request for Ollama's POST /api/generate endpoint.
/// </summary>
public sealed class OllamaGenerateRequest
{
    [JsonPropertyName("model")]
    public string Model { get; set; } = string.Empty;

    [JsonPropertyName("prompt")]
    public string Prompt { get; set; } = string.Empty;

    /// <summary>Optional system instruction.</summary>
    [JsonPropertyName("system")]
    public string? System { get; set; }

    /// <summary>
    /// We always disable streaming so the full answer arrives in a
    /// single JSON document, which is easier to consume from a Web API.
    /// </summary>
    [JsonPropertyName("stream")]
    public bool Stream { get; set; } = false;
}

