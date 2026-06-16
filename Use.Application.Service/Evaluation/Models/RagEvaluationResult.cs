namespace Use.Application.Service.Evaluation.Models;

/// <summary>
/// Per-case outcome of a retrieval-only evaluation run. Captures the hit info at
/// every stage plus the diagnosed <see cref="FailureStage"/>.
/// </summary>
public sealed class RagEvaluationResult
{
    public string CaseId { get; set; } = string.Empty;
    public string? CaseGroupId { get; set; }
    public string? VariantId { get; set; }
    public string Question { get; set; } = string.Empty;

    public string? QuestionType { get; set; }
    public string? RetrievalType { get; set; }
    public string? Difficulty { get; set; }
    public string? Answerability { get; set; }

    /// <summary>True when this case is an answerable, source-bearing case that
    /// contributes to the recall metrics.</summary>
    public bool CountedTowardRecall { get; set; }

    public string RetrievalMode { get; set; } = string.Empty;
    public bool RerankingApplied { get; set; }

    public RetrievalStageHitInfo Semantic { get; set; } = new();
    public RetrievalStageHitInfo Lexical { get; set; } = new();
    public RetrievalStageHitInfo Fusion { get; set; } = new();
    public RetrievalStageHitInfo Rerank { get; set; } = new();
    public RetrievalStageHitInfo SelectedDocument { get; set; } = new();
    public RetrievalStageHitInfo FinalContext { get; set; } = new();

    /// <summary>True when retrieval succeeded for this (counted) case.</summary>
    public bool Passed { get; set; }

    public EvaluationFailureStage FailureStage { get; set; } = EvaluationFailureStage.NoExpectedSourceDefined;

    /// <summary>Populated only when the case threw during evaluation.</summary>
    public string? Error { get; set; }

    public int DurationMs { get; set; }
}

