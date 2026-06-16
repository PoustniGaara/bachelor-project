using Use.Application.Service.Models.Retrieval;

namespace Use.Application.Service.Services.Rag.DocumentSelection;

/// <summary>
/// Groups hits by (sourceSystem, sourceDocumentId) and returns the N groups
/// with the highest chunk-hit count. Ties broken by best similarity score.
/// </summary>
public sealed class TopDocumentSelector : IDocumentSelector
{
    public IReadOnlyList<DocumentSelection> Select(
        IReadOnlyList<RetrievedChunk> initialHits,
        int topDocuments)
    {
        ArgumentNullException.ThrowIfNull(initialHits);
        if (topDocuments <= 0 || initialHits.Count == 0)
            return Array.Empty<DocumentSelection>();

        return initialHits
            .Where(c => !string.IsNullOrWhiteSpace(c.SourceDocumentId))
            .GroupBy(c => new
            {
                System = c.SourceSystem ?? string.Empty,
                DocId  = c.SourceDocumentId!
            })
            .Select(g => new DocumentSelection(
                SourceSystem:    g.Key.System,
                SourceDocumentId: g.Key.DocId,
                HitCount:        g.Count(),
                BestScore:       g.Max(x => x.Score),
                Title:           g.Select(x => x.SourceTitle).FirstOrDefault(t => !string.IsNullOrWhiteSpace(t)),
                Url:             g.Select(x => x.SourceUrl).FirstOrDefault(u => !string.IsNullOrWhiteSpace(u))))
            .OrderByDescending(d => d.HitCount)
            .ThenByDescending(d => d.BestScore)
            .Take(topDocuments)
            .ToList();
    }
}