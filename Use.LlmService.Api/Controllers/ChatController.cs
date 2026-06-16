using Microsoft.AspNetCore.Mvc;
using Use.LlmService.Api.Models;
using Use.LlmService.Api.Services;

namespace Use.LlmService.Api.Controllers;

/// <summary>
/// HTTP endpoint used by the Application Server to obtain LLM answers
/// for user prompts.
/// </summary>
[ApiController]
[Route("api/chat")]
public sealed class ChatController : ControllerBase
{
    private readonly IChatCompletionService _chatService;
    private readonly ILogger<ChatController> _logger;

    public ChatController(IChatCompletionService chatService, ILogger<ChatController> logger)
    {
        _chatService = chatService;
        _logger = logger;
    }

    /// <summary>POST /api/chat — generate an answer for the supplied prompt.</summary>
    [HttpPost]
    [ProducesResponseType(typeof(ChatResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<ActionResult<ChatResponse>> GenerateAnswer(
        [FromBody] ChatRequest request,
        CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Prompt))
            return BadRequest(new { error = "Field 'prompt' must not be empty." });

        try
        {
            var result = await _chatService.GenerateAnswerAsync(request, cancellationToken);
            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "Chat provider failed.");
            return StatusCode(StatusCodes.Status502BadGateway, new { error = ex.Message });
        }
    }
}

