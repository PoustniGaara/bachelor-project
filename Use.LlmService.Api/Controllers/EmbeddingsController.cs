using Microsoft.AspNetCore.Mvc;
using Use.LlmService.Api.Models;
using Use.LlmService.Api.Services;

namespace Use.LlmService.Api.Controllers;

/// <summary>
/// HTTP endpoint used by the Indexing Worker to turn document chunks
/// into embedding vectors.
/// </summary>
[ApiController]
[Route("api/embeddings")]
public sealed class EmbeddingsController : ControllerBase
{
    private readonly IEmbeddingService _embeddingService;
    private readonly ILogger<EmbeddingsController> _logger;

    public EmbeddingsController(IEmbeddingService embeddingService, ILogger<EmbeddingsController> logger)
    {
        _embeddingService = embeddingService;
        _logger = logger;
    }

    /// <summary>POST /api/embeddings — generate an embedding for the supplied text.</summary>
    [HttpPost]
    [ProducesResponseType(typeof(EmbeddingResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<ActionResult<EmbeddingResponse>> CreateEmbedding(
        [FromBody] EmbeddingRequest request,
        CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Input))
            return BadRequest(new { error = "Field 'input' must not be empty." });

        try
        {
            var result = await _embeddingService.CreateEmbeddingAsync(request, cancellationToken);
            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "Embedding provider failed.");
            return StatusCode(StatusCodes.Status502BadGateway, new { error = ex.Message });
        }
    }
}

