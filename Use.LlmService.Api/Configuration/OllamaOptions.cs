namespace Use.LlmService.Api.Configuration;

/// <summary>
/// Configuration for the local Ollama runtime that hosts the LLM and
/// embedding models. The values are bound from the "Ollama" section of
/// appsettings.json.
/// </summary>
public sealed class OllamaOptions
{
    public const string SectionName = "Ollama";

    /// <summary>Base URL of the Ollama HTTP API.</summary>
    public string BaseUrl { get; set; } = "http://localhost:11434";

    /// <summary>Model used to produce text embeddings.</summary>
    public string EmbeddingModel { get; set; } = "embeddinggemma";

    /// <summary>Model used to generate chat answers.</summary>
    public string ChatModel { get; set; } = "gemma3:4b";
}

