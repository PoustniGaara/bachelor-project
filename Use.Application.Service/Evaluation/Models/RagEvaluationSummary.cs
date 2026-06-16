namespace Use.Application.Service.Evaluation.Models;

/// <summary>Recall metrics aggregated over a subset of cases (a question type or difficulty).</summary>
public sealed class MetricBucket
{
    public int TotalCases { get; set; }
    public int CountedCases { get; set; }
    public int PassedCases { get; set; }
    public double FinalContextRecall { get; set; }
    public double SelectedDocumentRecall { get; set; }
    public double FusionRecall { get; set; }
    public double RerankRecall { get; set; }
}

/// <summary>Top-level report of one retrieval evaluation run.</summary>
public sealed class RagEvaluationSummary
{
    public string RunId { get; set; } = string.Empty;
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset FinishedAt { get; set; }
    public long DurationMs { get; set; }

    public int TotalCases { get; set; }
    public int EvaluatedCases { get; set; }
    public int SkippedCases { get; set; }
    public int PassedCases { get; set; }
    public int FailedCases { get; set; }

    /// <summary>Number of cases that contribute to the recall denominators.</summary>
    public int CountedCases { get; set; }

    public double FinalContextRecall { get; set; }
    public double SelectedDocumentRecall { get; set; }
    public double FusionRecall { get; set; }
    public double RerankRecall { get; set; }

    public Dictionary<string, MetricBucket> MetricsByQuestionType { get; set; } = new();
    public Dictionary<string, MetricBucket> MetricsByDifficulty { get; set; } = new();
    public Dictionary<string, int> FailureStageCounts { get; set; } = new();

    public List<RagEvaluationResult> Results { get; set; } = new();
}

