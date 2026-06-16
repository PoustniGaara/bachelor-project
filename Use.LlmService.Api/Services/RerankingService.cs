using Use.LlmService.Api.Models;
using Use.LlmService.Api.Providers.Reranking;

namespace Use.LlmService.Api.Services;

/// <summary>
/// Default implementation that delegates to a configured
/// <see cref="IRerankingProvider"/>. The concrete provider is selected in
/// Program.cs based on <c>Reranker:Provider</c>.
/// </summary>
public sealed class RerankingService : IRerankingService
{
    private readonly IRerankingProvider _provider;
    private readonly ILogger<RerankingService> _logger;

    public RerankingService(IRerankingProvider provider, ILogger<RerankingService> logger)
    {
        _provider = provider;
        _logger = logger;
    }

    public async Task<RerankResponse> RerankAsync(RerankRequest request, CancellationToken cancellationToken = default)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));
        if (string.IsNullOrWhiteSpace(request.Query))
            throw new ArgumentException("Query must not be empty.", nameof(request));
        if (request.Documents is null || request.Documents.Count == 0)
            throw new ArgumentException("Documents must not be empty.", nameof(request));

        for (var i = 0; i < request.Documents.Count; i++)
        {
            var document = request.Documents[i];
            if (string.IsNullOrWhiteSpace(document.ChunkId))
                throw new ArgumentException($"documents[{i}].chunkId must not be empty.", nameof(request));
            if (string.IsNullOrWhiteSpace(document.Text))
                throw new ArgumentException($"documents[{i}].text must not be empty.", nameof(request));
        }

        _logger.LogInformation(
            "Reranking via {Provider}: queryLength={QueryLength}, documents={Count}",
            _provider.Name, request.Query.Length, request.Documents.Count);

        return await _provider.RerankAsync(request.Query, request.Documents, cancellationToken);
    }
}

