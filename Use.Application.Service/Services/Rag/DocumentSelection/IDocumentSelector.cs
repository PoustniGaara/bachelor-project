using Use.Application.Service.Models.Retrieval;

namespace Use.Application.Service.Services.Rag.DocumentSelection;

/// <summary>
/// Picks the most relevant source documents out of an initial set of vector hits.
/// </summary>
public interface IDocumentSelector
{
    IReadOnlyList<DocumentSelection> Select(
        IReadOnlyList<RetrievedChunk> initialHits,
        int topDocuments);
}

public sealed record DocumentSelection(
    string SourceSystem,
    string SourceDocumentId,
    int HitCount,
    float BestScore,
    string? Title,
    string? Url,
    double DocumentScore = 0d,
    string? RepresentativeChunkId = null);
