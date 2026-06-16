using Use.LlmService.Api.Models;
using Use.LlmService.Api.Providers.Chat;

namespace Use.LlmService.Api.Services;

/// <summary>
/// Default implementation that delegates to a configured
/// <see cref="IChatProvider"/>. The concrete provider is selected
/// in Program.cs based on <c>Llm:ChatProvider</c>.
/// </summary>
public sealed class ChatCompletionService : IChatCompletionService
{
    private readonly IChatProvider _provider;
    private readonly ILogger<ChatCompletionService> _logger;

    public ChatCompletionService(IChatProvider provider, ILogger<ChatCompletionService> logger)
    {
        _provider = provider;
        _logger = logger;
    }

    public async Task<ChatResponse> GenerateAnswerAsync(ChatRequest request, CancellationToken cancellationToken = default)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));
        if (string.IsNullOrWhiteSpace(request.Prompt))
            throw new ArgumentException("Prompt must not be empty.", nameof(request));

        _logger.LogInformation(
            "Generating chat answer via {Provider}, promptLength={Length}, hasSystemPrompt={HasSystem}",
            _provider.Name, request.Prompt.Length, !string.IsNullOrWhiteSpace(request.SystemPrompt));

        return await _provider.GenerateAnswerAsync(request.Prompt, request.SystemPrompt, cancellationToken);
    }
}

