using Use.Application.Service.Models.Retrieval;

namespace Use.Application.Service.Services.Rag.DocumentSelection;

/// <summary>
/// Groups fused chunks by <c>(sourceSystem, sourceDocumentId)</c> and ranks the
/// documents by an aggregate score.
///
/// <para>
/// When reranker scores are available on the candidate chunks (i.e. reranking
/// ran), the document score is driven by the reranker:
/// <code>
/// DocumentScore = BestRerankScore + TopRerankScoreSum(top 3) + HitCount * 0.01
/// </code>
/// Otherwise it falls back to the legacy fused-score formula:
/// <code>
/// DocumentScore = BestScore + TopScoreSum(top 3) + HitCount * 0.01
/// </code>
/// Tie-breaks: BestScore desc, then HitCount desc. This rewards a document with
/// one very strong hit as well as documents with several decent hits, and keeps
/// full compatibility with <c>Rag:RerankingEnabled=false</c>.
/// </para>
/// </summary>
public sealed class HybridDocumentSelector : IHybridDocumentSelector
{
    private const double HitCountBonus = 0.01d;
    private const int TopScoresToSum = 3;

    public IReadOnlyList<DocumentSelection> Select(
        IReadOnlyList<FusedChunkResult> fusedChunks,
        int topDocuments)
    {
        ArgumentNullException.ThrowIfNull(fusedChunks);
        if (topDocuments <= 0 || fusedChunks.Count == 0)
            return Array.Empty<DocumentSelection>();

        return fusedChunks
            .Where(c => !string.IsNullOrWhiteSpace(c.SourceDocumentId))
            .GroupBy(c => new
            {
                System = c.SourceSystem,
                DocId = c.SourceDocumentId
            })
            .Select(g =>
            {
                var hitCount = g.Count();
                var rerankScores = g
                    .Where(x => x.WasReranked && x.RerankScore.HasValue)
                    .Select(x => x.RerankScore!.Value)
                    .ToList();

                // BestScore (used for the source reference + tie-breaks) stays the
                // best fused score so existing telemetry semantics are preserved.
                var byFused = g.OrderByDescending(x => x.FusedScore).ToList();
                var bestFusedScore = byFused[0].FusedScore;

                double documentScore;
                FusedChunkResult representative;

                if (rerankScores.Count > 0)
                {
                    // Reranker-driven document scoring.
                    var byRerank = g.OrderByDescending(x => x.RerankScore ?? double.MinValue).ToList();
                    var bestRerankScore = rerankScores.Max();
                    var topRerankScoreSum = rerankScores
                        .OrderByDescending(s => s)
                        .Take(TopScoresToSum)
                        .Sum();

                    documentScore = bestRerankScore + topRerankScoreSum + hitCount * HitCountBonus;
                    representative = byRerank[0];
                }
                else
                {
                    // Legacy fused-score document scoring.
                    var topScoreSum = byFused.Take(TopScoresToSum).Sum(x => x.FusedScore);
                    documentScore = bestFusedScore + topScoreSum + hitCount * HitCountBonus;
                    representative = byFused[0];
                }

                return new DocumentSelection(
                    SourceSystem: g.Key.System,
                    SourceDocumentId: g.Key.DocId,
                    HitCount: hitCount,
                    BestScore: (float)bestFusedScore,
                    Title: g.Select(x => x.Title).FirstOrDefault(t => !string.IsNullOrWhiteSpace(t)),
                    Url: g.Select(x => x.Url).FirstOrDefault(u => !string.IsNullOrWhiteSpace(u)),
                    DocumentScore: documentScore,
                    RepresentativeChunkId: representative.ChunkId);
            })
            .OrderByDescending(d => d.DocumentScore)
            .ThenByDescending(d => d.BestScore)
            .ThenByDescending(d => d.HitCount)
            .Take(topDocuments)
            .ToList();
    }
}

