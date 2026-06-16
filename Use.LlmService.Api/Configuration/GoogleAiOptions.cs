using System.ComponentModel.DataAnnotations;

namespace Use.LlmService.Api.Configuration;

/// <summary>
/// Configuration for Google's Generative Language API, which hosts the
/// cloud Gemma models (e.g. "gemma-4-31b-it"). Values are bound from
/// the "GoogleAi" section of appsettings.json. The API key should
/// normally come from user secrets / environment variables, not source
/// control.
/// </summary>
public sealed class GoogleAiOptions
{
    public const string SectionName = "GoogleAi";

    /// <summary>Base URL of the Generative Language API.</summary>
    public string BaseUrl { get; set; } = "https://generativelanguage.googleapis.com/v1beta/";

    /// <summary>
    /// Model id used for chat completions. Examples:
    /// "gemma-4-31b-it", "gemma-3-27b-it".
    /// </summary>
    public string ChatModel { get; set; } = "gemma-4-31b-it";

    /// <summary>
    /// API key issued by Google AI Studio. Required when the GoogleAi
    /// provider is selected.
    /// </summary>
    [Required(AllowEmptyStrings = false)]
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Sampling temperature (0.0 - 2.0).</summary>
    public double Temperature { get; set; } = 0.2;

    /// <summary>Maximum number of output tokens.</summary>
    public int MaxOutputTokens { get; set; } = 2048;

    /// <summary>
    /// Thinking budget for reasoning-capable Gemma models.
    /// One of: "off", "low", "medium", "high".
    /// </summary>
    public string ThinkingLevel { get; set; } = "high";
}

