using System.ComponentModel.DataAnnotations;

namespace Use.Application.Service.Configuration;

/// <summary>
/// Settings used to talk to the Use.LlmService HTTP API
/// (embedding + generation endpoints).
/// </summary>
public sealed class LlmServiceOptions
{
    public const string SectionName = "LlmService";

    /// <summary>Root URL of the LLM service, e.g. http://localhost:5133.</summary>
    [Required]
    public string BaseUrl { get; set; } = "http://localhost:5133";

    /// <summary>Relative path of the embeddings endpoint.</summary>
    public string EmbeddingEndpoint { get; set; } = "/api/embeddings";

    /// <summary>Relative path of the chat / generation endpoint.</summary>
    public string GenerationEndpoint { get; set; } = "/api/chat";

    /// <summary>Relative path of the reranking endpoint.</summary>
    public string RerankEndpoint { get; set; } = "/api/rerank";

    /// <summary>HTTP timeout for embedding calls.</summary>
    public TimeSpan EmbeddingTimeout { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>HTTP timeout for generation calls.</summary>
    public TimeSpan GenerationTimeout { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>HTTP timeout for reranking calls.</summary>
    public TimeSpan RerankTimeout { get; set; } = TimeSpan.FromMinutes(2);
}

