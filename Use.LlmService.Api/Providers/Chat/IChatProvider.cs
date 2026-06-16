using Use.LlmService.Api.Models;

namespace Use.LlmService.Api.Providers.Chat;

/// <summary>
/// Abstraction over a concrete chat completion backend
/// (Ollama today, OpenAI / Azure OpenAI in the future).
/// </summary>
public interface IChatProvider
{
    /// <summary>Logical name of the provider (e.g. "Ollama").</summary>
    string Name { get; }

    /// <summary>Generate an answer for the given prompt.</summary>
    Task<ChatResponse> GenerateAnswerAsync(string prompt, string? systemPrompt, CancellationToken cancellationToken = default);
}

