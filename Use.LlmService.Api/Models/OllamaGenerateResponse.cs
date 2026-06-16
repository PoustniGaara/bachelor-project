using System.Text.Json.Serialization;

namespace Use.LlmService.Api.Models;

/// <summary>
/// Wire-format response from Ollama's POST /api/generate endpoint
/// (with stream = false).
/// </summary>
public sealed class OllamaGenerateResponse
{
    [JsonPropertyName("model")]
    public string? Model { get; set; }

    [JsonPropertyName("response")]
    public string? Response { get; set; }

    [JsonPropertyName("done")]
    public bool Done { get; set; }
}

