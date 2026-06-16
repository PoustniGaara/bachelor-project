using Use.Application.Service.Configuration;
using Use.Application.Service.Evaluation.Models;

namespace Use.Application.Service.Evaluation;

/// <summary>Computes per-case retrieval metrics + failure diagnosis from a probe result.</summary>
public interface IRetrievalEvaluator
{
    RagEvaluationResult Evaluate(RagEvaluationCase evaluationCase, RetrievalProbeResult probe);
}

/// <summary>
/// Default <see cref="IRetrievalEvaluator"/>. Stateless; applies the documented
/// chunk-first / document-fallback matching rules and the failure-stage logic.
/// </summary>
public sealed class RetrievalEvaluator : IRetrievalEvaluator
{
    public RagEvaluationResult Evaluate(RagEvaluationCase c, RetrievalProbeResult probe)
    {
        ArgumentNullException.ThrowIfNull(c);
        ArgumentNullException.ThrowIfNull(probe);

        var result = new RagEvaluationResult
        {
            CaseId = c.CaseId,
            CaseGroupId = c.CaseGroupId,
            VariantId = c.VariantId,
            Question = c.Question,
            QuestionType = c.QuestionType,
            RetrievalType = c.RetrievalType,
            Difficulty = c.Difficulty,
            Answerability = c.Answerability,
            RetrievalMode = probe.Mode.ToString(),
            RerankingApplied = probe.RerankingApplied,
            Semantic = EvaluateChunkStage(c, probe.SemanticCandidates),
            Lexical = EvaluateChunkStage(c, probe.LexicalCandidates),
            Fusion = EvaluateChunkStage(c, probe.FusedCandidates),
            Rerank = EvaluateChunkStage(c, probe.RerankedCandidates),
            SelectedDocument = EvaluateDocumentStage(c, probe.SelectedDocuments),
            FinalContext = EvaluateChunkStage(c, probe.FinalContextChunks)
        };

        var hasTargets = c.TargetChunkIds.Count > 0 || c.TargetSourceDocumentIds.Count > 0;
        result.CountedTowardRecall = CountsTowardRecall(c, hasTargets);

        DiagnoseFailureStage(result, probe.Mode, hasTargets);
        return result;
    }

    /// <summary>An evaluation result for a case that threw while probing.</summary>
    public static RagEvaluationResult Errored(RagEvaluationCase c, string error)
    {
        var hasTargets = c.TargetChunkIds.Count > 0 || c.TargetSourceDocumentIds.Count > 0;
        return new RagEvaluationResult
        {
            CaseId = c.CaseId,
            CaseGroupId = c.CaseGroupId,
            VariantId = c.VariantId,
            Question = c.Question,
            QuestionType = c.QuestionType,
            RetrievalType = c.RetrievalType,
            Difficulty = c.Difficulty,
            Answerability = c.Answerability,
            CountedTowardRecall = CountsTowardRecall(c, hasTargets),
            Passed = false,
            FailureStage = EvaluationFailureStage.EvaluationError,
            Error = error
        };
    }

    private static bool CountsTowardRecall(RagEvaluationCase c, bool hasTargets)
    {
        if (!hasTargets) return false;
        if (Eq(c.RetrievalType, "negative_no_source_expected")) return false;
        if (Eq(c.QuestionType, "out_of_scope")) return false;

        // Default to "answerable" when answerability is unspecified but targets exist.
        var answerable = c.Answerability is null
            ? true
            : Eq(c.Answerability, "answerable");

        return answerable;
    }

    private static void DiagnoseFailureStage(RagEvaluationResult r, RetrievalMode mode, bool hasTargets)
    {
        if (!r.CountedTowardRecall)
        {
            // Negative / ambiguous / out-of-scope / no-target cases are informational.
            r.FailureStage = EvaluationFailureStage.NoExpectedSourceDefined;
            r.Passed = !hasTargets ? true : !r.FinalContext.Hit; // negative cases "pass" when nothing was retrieved
            return;
        }

        if (r.FinalContext.Hit)
        {
            r.FailureStage = EvaluationFailureStage.Passed;
            r.Passed = true;
            return;
        }

        r.Passed = false;

        if (!r.Fusion.Hit && !r.Semantic.Hit && !r.Lexical.Hit)
        {
            r.FailureStage = mode == RetrievalMode.LexicalOnly
                ? EvaluationFailureStage.MissedByLexical
                : EvaluationFailureStage.MissedBySemantic;
        }
        else if (!r.Fusion.Hit)
        {
            r.FailureStage = EvaluationFailureStage.MissedAfterFusion;
        }
        else if (r.RerankingApplied && !r.Rerank.Hit)
        {
            r.FailureStage = EvaluationFailureStage.LostAfterRerank;
        }
        else if (!r.SelectedDocument.Hit)
        {
            r.FailureStage = EvaluationFailureStage.LostInDocumentSelection;
        }
        else
        {
            // Target document selected but its chunk never reached the context.
            r.FailureStage = EvaluationFailureStage.LostInContextAssembly;
        }
    }

    // Chunk-first matching: when targetChunkIds exist, match on chunkId only;
    // otherwise fall back to document-level matching on sourceDocumentId.
    private static RetrievalStageHitInfo EvaluateChunkStage(
        RagEvaluationCase c, IReadOnlyList<ProbeCandidate> candidates)
    {
        var info = new RetrievalStageHitInfo { CandidateCount = candidates.Count };

        if (c.TargetChunkIds.Count > 0)
        {
            info.Applicable = true;
            var targets = new HashSet<string>(c.TargetChunkIds, StringComparer.Ordinal);
            foreach (var cand in candidates.OrderBy(x => x.Rank))
            {
                if (!targets.Contains(cand.ChunkId)) continue;
                info.Hit = true;
                info.BestRank = cand.Rank;
                info.MatchedId = cand.ChunkId;
                info.MatchLevel = "chunk";
                break;
            }
            return info;
        }

        if (c.TargetSourceDocumentIds.Count > 0)
        {
            info.Applicable = true;
            var targets = new HashSet<string>(c.TargetSourceDocumentIds, StringComparer.Ordinal);
            foreach (var cand in candidates.OrderBy(x => x.Rank))
            {
                if (!targets.Contains(cand.SourceDocumentId)) continue;
                info.Hit = true;
                info.BestRank = cand.Rank;
                info.MatchedId = cand.SourceDocumentId;
                info.MatchLevel = "document";
                break;
            }
            return info;
        }

        info.Applicable = false;
        return info;
    }

    private static RetrievalStageHitInfo EvaluateDocumentStage(
        RagEvaluationCase c, IReadOnlyList<ProbeDocument> documents)
    {
        var info = new RetrievalStageHitInfo { CandidateCount = documents.Count };
        if (c.TargetSourceDocumentIds.Count == 0)
        {
            info.Applicable = false;
            return info;
        }

        info.Applicable = true;
        var targets = new HashSet<string>(c.TargetSourceDocumentIds, StringComparer.Ordinal);
        var rank = 0;
        foreach (var d in documents)
        {
            rank++;
            if (!targets.Contains(d.SourceDocumentId)) continue;
            info.Hit = true;
            info.BestRank = rank;
            info.MatchedId = d.SourceDocumentId;
            info.MatchLevel = "document";
            break;
        }
        return info;
    }

    private static bool Eq(string? value, string other)
        => string.Equals(value, other, StringComparison.OrdinalIgnoreCase);
}

