namespace Use.Application.Service.Evaluation.Models;

/// <summary>
/// One line of the JSONL evaluation dataset. Mirrors the documented schema.
/// Property names are matched case-insensitively against the camelCase JSON.
/// </summary>
public sealed class RagEvaluationCase
{
    public string CaseId { get; set; } = string.Empty;
    public string? CaseGroupId { get; set; }
    public string? VariantId { get; set; }
    public bool Enabled { get; set; } = true;

    public string Question { get; set; } = string.Empty;
    public string? Language { get; set; }

    public string? QuestionType { get; set; }
    public string? RetrievalType { get; set; }
    public string? Difficulty { get; set; }
    public string? Answerability { get; set; }

    public string? ExpectedBehavior { get; set; }

    public string? TargetSourceSystem { get; set; }
    public List<string> TargetSourceDocumentIds { get; set; } = new();
    public List<string> TargetChunkIds { get; set; } = new();

    public string? TargetTitle { get; set; }
    public string? TargetPath { get; set; }
    public string? TargetUrl { get; set; }

    public List<string> ExpectedAnswerFacts { get; set; } = new();
    public List<string> AcceptableAnswerPatterns { get; set; } = new();
    public List<string> MustNotContain { get; set; } = new();

    public string? Notes { get; set; }
}

