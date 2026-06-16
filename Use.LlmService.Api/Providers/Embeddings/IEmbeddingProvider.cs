using Use.LlmService.Api.Models;

namespace Use.LlmService.Api.Providers.Embeddings;

/// <summary>
/// Abstraction over a concrete embedding backend (Ollama, OpenAI, ...).
/// Application services depend on this interface, not on a specific
/// provider, so swapping or adding providers does not affect callers.
/// </summary>
public interface IEmbeddingProvider
{
    /// <summary>Logical name of the provider (e.g. "Ollama").</summary>
    string Name { get; }

    /// <summary>Generate an embedding vector for the given text.</summary>
    Task<EmbeddingResponse> GenerateEmbeddingAsync(string input, CancellationToken cancellationToken = default);
}

