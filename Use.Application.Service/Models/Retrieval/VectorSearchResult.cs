namespace Use.Application.Service.Models.Retrieval;

/// <summary>
/// Aggregated outcome of a vector-search call.
/// </summary>
public sealed class VectorSearchResult
{
    public IReadOnlyList<RetrievedChunk> Chunks { get; set; } = Array.Empty<RetrievedChunk>();
    public string CollectionName { get; set; } = string.Empty;
    public int RequestedTopK { get; set; }
}

