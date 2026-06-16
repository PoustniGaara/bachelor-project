using Use.LlmService.Api.Models;

namespace Use.LlmService.Api.Services;

/// <summary>
/// Application-level service used by the ChatController.
/// Decouples the controller from the chat provider implementation.
/// </summary>
public interface IChatCompletionService
{
    Task<ChatResponse> GenerateAnswerAsync(ChatRequest request, CancellationToken cancellationToken = default);
}

