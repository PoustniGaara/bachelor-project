namespace Use.LlmService.Api.Configuration;

/// <summary>
/// Strongly typed options that select which provider implementation
/// is used for embeddings and chat. This enables future support for
/// additional providers (e.g. OpenAI, Azure OpenAI) without changing
/// the controllers or the application services.
/// </summary>
public sealed class LlmOptions
{
    public const string SectionName = "Llm";

    /// <summary>Provider used to generate embeddings (e.g. "Ollama").</summary>
    public string EmbeddingProvider { get; set; } = "Ollama";

    /// <summary>Provider used for chat completions (e.g. "Ollama").</summary>
    public string ChatProvider { get; set; } = "Ollama";
}

