using System.Text.Json.Serialization;

namespace Use.LlmService.Api.Models;

/// <summary>
/// Wire-format request for Ollama's POST /api/embed endpoint.
/// </summary>
public sealed class OllamaEmbedRequest
{
    [JsonPropertyName("model")]
    public string Model { get; set; } = string.Empty;

    /// <summary>
    /// Ollama accepts either a single string or an array of strings.
    /// We always send a single string here.
    /// </summary>
    [JsonPropertyName("input")]
    public string Input { get; set; } = string.Empty;
}

