using Microsoft.AspNetCore.Mvc;
using Use.LlmService.Api.Models;
using Use.LlmService.Api.Services;

namespace Use.LlmService.Api.Controllers;

/// <summary>
/// HTTP endpoint used by the Application Service to reorder candidate chunks by
/// relevance to a query. Forwards to the configured reranking provider, hiding
/// the concrete model (BGE, Cohere, ...) behind a provider-agnostic contract.
/// </summary>
[ApiController]
[Route("api/rerank")]
public sealed class RerankController : ControllerBase
{
    private readonly IRerankingService _rerankingService;
    private readonly ILogger<RerankController> _logger;

    public RerankController(IRerankingService rerankingService, ILogger<RerankController> logger)
    {
        _rerankingService = rerankingService;
        _logger = logger;
    }

    /// <summary>POST /api/rerank — score and reorder the supplied documents.</summary>
    [HttpPost]
    [ProducesResponseType(typeof(RerankResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<ActionResult<RerankResponse>> Rerank(
        [FromBody] RerankRequest request,
        CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Query))
            return BadRequest(new { error = "Field 'query' must not be empty." });

        if (request.Documents is null || request.Documents.Count == 0)
            return BadRequest(new { error = "Field 'documents' must not be empty." });

        try
        {
            var result = await _rerankingService.RerankAsync(request, cancellationToken);
            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "Reranking provider failed.");
            return StatusCode(StatusCodes.Status502BadGateway, new { error = ex.Message });
        }
    }
}

