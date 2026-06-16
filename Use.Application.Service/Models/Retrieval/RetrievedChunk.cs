namespace Use.Application.Service.Models.Retrieval;

/// <summary>
/// One chunk pulled from the vector store, normalised into the shape the
/// orchestrator and prompt builder consume.
/// </summary>
public sealed class RetrievedChunk
{
    /// <summary>Stable id assigned by the indexer (e.g. "WikiJs:265:0").</summary>
    public string ChunkId { get; set; } = string.Empty;

    /// <summary>Cosine / dot similarity score returned by Qdrant.</summary>
    public float Score { get; set; }

    /// <summary>Plain-text content of the chunk.</summary>
    public string Text { get; set; } = string.Empty;

    public string? SourceTitle { get; set; }
    public string? SourceUrl { get; set; }
    public string? SourceSystem { get; set; }
    public string? SourceDocumentId { get; set; }
    
    /// <summary>Position of this chunk inside its source document (0-based).</summary>
    public int? ChunkOrder { get; set; }

    /// <summary>Any additional payload fields kept around for debugging.</summary>
    public IReadOnlyDictionary<string, string> Metadata { get; set; }
        = new Dictionary<string, string>();
}

