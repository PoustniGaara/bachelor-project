using System.ComponentModel.DataAnnotations;

namespace Use.Application.Service.Common;

/// <summary>
/// DTO sent to the LLM service's <c>POST /api/chat</c> endpoint.
/// Must match <c>Use.LlmService.Api.Models.ChatRequest</c>.
/// </summary>
public sealed class GenerationRequest
{
    [Required]
    public string Prompt { get; set; } = string.Empty;

    public string? SystemPrompt { get; set; }
}

