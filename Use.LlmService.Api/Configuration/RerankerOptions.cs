namespace Use.LlmService.Api.Configuration;

/// <summary>
/// Configuration for the reranking capability. Values are bound from the
/// "Reranker" section of appsettings.json. The reranker is hidden behind the
/// gateway's <c>/api/rerank</c> endpoint, so callers never know which backend
/// (BGE, Cohere, an LLM-as-judge, ...) is actually used.
/// </summary>
public sealed class RerankerOptions
{
    public const string SectionName = "Reranker";

    /// <summary>
    /// Which provider performs reranking. Supported today: <c>Bge</c>.
    /// </summary>
    public string Provider { get; set; } = "Bge";

    /// <summary>Base URL of the reranker backend (the Python FastAPI service).</summary>
    public string BaseUrl { get; set; } = "http://localhost:8000";

    /// <summary>Relative path of the reranking endpoint on the backend.</summary>
    public string Endpoint { get; set; } = "/rerank";

    /// <summary>HTTP timeout for reranking calls.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>Logical model id, reported in responses and used for logging.</summary>
    public string Model { get; set; } = "BAAI/bge-reranker-v2-m3";
}

