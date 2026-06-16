namespace Use.LlmService.Api.Models;

/// <summary>
/// Provider-agnostic embedding response returned to API clients.
/// </summary>
public sealed class EmbeddingResponse
{
    /// <summary>Name of the model that produced the embedding.</summary>
    public string Model { get; set; } = string.Empty;

    /// <summary>Number of components in the embedding vector.</summary>
    public int Dimensions { get; set; }

    /// <summary>The embedding vector itself.</summary>
    public float[] Embedding { get; set; } = [];
}

