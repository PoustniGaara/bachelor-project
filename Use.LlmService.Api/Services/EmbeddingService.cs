using Use.LlmService.Api.Models;
using Use.LlmService.Api.Providers.Embeddings;

namespace Use.LlmService.Api.Services;

/// <summary>
/// Default implementation that delegates to a configured
/// <see cref="IEmbeddingProvider"/>. The concrete provider is selected
/// in Program.cs based on <c>Llm:EmbeddingProvider</c>.
/// </summary>
public sealed class EmbeddingService : IEmbeddingService
{
    private readonly IEmbeddingProvider _provider;
    private readonly ILogger<EmbeddingService> _logger;

    public EmbeddingService(IEmbeddingProvider provider, ILogger<EmbeddingService> logger)
    {
        _provider = provider;
        _logger = logger;
    }

    public async Task<EmbeddingResponse> CreateEmbeddingAsync(EmbeddingRequest request, CancellationToken cancellationToken = default)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));
        if (string.IsNullOrWhiteSpace(request.Input))
            throw new ArgumentException("Input must not be empty.", nameof(request));

        _logger.LogInformation(
            "Generating embedding via {Provider} for sourceType={SourceType}, length={Length}",
            _provider.Name, request.SourceType ?? "(unspecified)", request.Input.Length);

        return await _provider.GenerateEmbeddingAsync(request.Input, cancellationToken);
    }
}

