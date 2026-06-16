using Use.LlmService.Api.Models;

namespace Use.LlmService.Api.Services;

/// <summary>
/// Application-level service used by the RerankController. Sits between the
/// controller and the reranking provider, so we can add cross-cutting concerns
/// (validation, logging, telemetry) without touching the controller or the
/// providers.
/// </summary>
public interface IRerankingService
{
    Task<RerankResponse> RerankAsync(RerankRequest request, CancellationToken cancellationToken = default);
}

