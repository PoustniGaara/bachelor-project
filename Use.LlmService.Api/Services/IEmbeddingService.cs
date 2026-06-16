using Use.LlmService.Api.Models;

namespace Use.LlmService.Api.Services;

/// <summary>
/// Application-level service used by the EmbeddingsController.
/// Sits between the controller and the embedding provider, so we can
/// add cross-cutting concerns (validation, caching, telemetry) without
/// touching the controller or the providers.
/// </summary>
public interface IEmbeddingService
{
    Task<EmbeddingResponse> CreateEmbeddingAsync(EmbeddingRequest request, CancellationToken cancellationToken = default);
}

