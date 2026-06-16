namespace Use.Application.Service.Models.Responses;

/// <summary>
/// Lightweight pointer to a documentation source backing a generated answer.
/// </summary>
public sealed class SourceReference
{
    public string? Title { get; set; }
    public string? Url { get; set; }
    public string? SourceSystem { get; set; }
    public string? ChunkId { get; set; }
    public float Score { get; set; }
}

