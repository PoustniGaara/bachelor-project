using System.Text.Json.Serialization;

namespace Use.LlmService.Api.Models;

/// <summary>
/// Wire-format request for Google's Generative Language API
/// (POST /v1beta/models/{model}:generateContent).
/// </summary>
public sealed class GoogleGenerateContentRequest
{
    [JsonPropertyName("systemInstruction")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public GoogleContent? SystemInstruction { get; set; }

    [JsonPropertyName("contents")] public List<GoogleContent> Contents { get; set; } = new();

    [JsonPropertyName("generationConfig")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public GoogleGenerationConfig? GenerationConfig { get; set; }
}

public sealed class GoogleContent
{
    [JsonPropertyName("role")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Role { get; set; }

    [JsonPropertyName("parts")]
    public List<GooglePart> Parts { get; set; } = new();
}

public sealed class GooglePart
{
    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;
    [JsonPropertyName("thought")]
    public bool? Thought { get; set; }
}

public sealed class GoogleGenerationConfig
{
    [JsonPropertyName("temperature")]
    public double Temperature { get; set; }

    [JsonPropertyName("maxOutputTokens")]
    public int MaxOutputTokens { get; set; }

    [JsonPropertyName("thinkingConfig")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public GoogleThinkingConfig? ThinkingConfig { get; set; }
}

public sealed class GoogleThinkingConfig
{
    [JsonPropertyName("thinkingLevel")]
    public string ThinkingLevel { get; set; } = "high";
}

