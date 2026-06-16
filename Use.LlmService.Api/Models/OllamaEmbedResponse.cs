using System.Text.Json.Serialization;

namespace Use.LlmService.Api.Models;

/// <summary>
/// Wire-format response from Ollama's POST /api/embed endpoint.
/// Ollama returns embeddings as a 2D array because batching is supported.
/// </summary>
public sealed class OllamaEmbedResponse
{
    [JsonPropertyName("model")]
    public string? Model { get; set; }

    [JsonPropertyName("embeddings")]
    public float[][]? Embeddings { get; set; }
}

