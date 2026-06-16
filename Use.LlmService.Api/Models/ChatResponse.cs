namespace Use.LlmService.Api.Models;

/// <summary>
/// Provider-agnostic chat completion response returned to API clients.
/// </summary>
public sealed class ChatResponse
{
    /// <summary>Name of the model that produced the answer.</summary>
    public string Model { get; set; } = string.Empty;

    /// <summary>Generated answer text.</summary>
    public string Answer { get; set; } = string.Empty;
}

