using System.ComponentModel.DataAnnotations;

namespace Use.LlmService.Api.Models;

/// <summary>
/// Request used by the Application Server to ask the LLM a question.
/// </summary>
public sealed class ChatRequest
{
    /// <summary>The user prompt / question.</summary>
    [Required]
    public string Prompt { get; set; } = string.Empty;

    /// <summary>Optional system instruction that steers the model.</summary>
    public string? SystemPrompt { get; set; }
}

