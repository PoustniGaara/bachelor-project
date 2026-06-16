namespace Use.Application.Service.Configuration;

/// <summary>
/// Connection + collection settings for the Qdrant vector database.
/// Port 6334 is the gRPC endpoint (preferred by Qdrant.Client).
/// Mirrors <c>Use.Indexing.Worker.Configuration.QdrantOptions</c> so both
/// services agree on the same logical collection.
/// </summary>
public sealed class QdrantOptions
{
    public const string SectionName = "Qdrant";

    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 6334;
    public bool UseHttps { get; set; } = false;
    public string? ApiKey { get; set; }

    /// <summary>Logical collection that holds all chunk vectors.</summary>
    public string CollectionName { get; set; } = "documentation_chunks";

    /// <summary>How many chunks to retrieve per query by default.</summary>
    public int TopK { get; set; } = 5;
}

