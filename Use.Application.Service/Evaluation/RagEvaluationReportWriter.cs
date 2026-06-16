using System.Globalization;
using System.Text;
using System.Text.Json;
using Use.Application.Service.Evaluation.Models;

namespace Use.Application.Service.Evaluation;

/// <summary>Persists an evaluation summary to disk (JSON + CSV).</summary>
public interface IRagEvaluationReportWriter
{
    /// <summary>Writes the report files and returns the JSON report path.</summary>
    Task<string> WriteAsync(
        RagEvaluationSummary summary, string outputDirectory, CancellationToken cancellationToken);
}

/// <summary>Default writer: a rich JSON report plus a flat CSV of per-case results.</summary>
public sealed class RagEvaluationReportWriter : IRagEvaluationReportWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DictionaryKeyPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly ILogger<RagEvaluationReportWriter> _logger;

    public RagEvaluationReportWriter(ILogger<RagEvaluationReportWriter> logger) => _logger = logger;

    public async Task<string> WriteAsync(
        RagEvaluationSummary summary, string outputDirectory, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(outputDirectory);

        var stamp = summary.StartedAt.ToLocalTime().ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var jsonPath = Path.Combine(outputDirectory, $"rag-evaluation-report-{stamp}.json");
        var csvPath = Path.Combine(outputDirectory, $"rag-evaluation-results-{stamp}.csv");

        await using (var stream = File.Create(jsonPath))
        {
            await JsonSerializer.SerializeAsync(stream, summary, JsonOptions, cancellationToken)
                .ConfigureAwait(false);
        }

        await File.WriteAllTextAsync(csvPath, BuildCsv(summary), cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("Evaluation report written to {JsonPath} (CSV: {CsvPath}).", jsonPath, csvPath);
        return jsonPath;
    }

    private static string BuildCsv(RagEvaluationSummary summary)
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(',',
            "caseId", "caseGroupId", "variantId", "questionType", "retrievalType", "difficulty",
            "answerability", "counted", "passed", "failureStage",
            "semanticHit", "semanticBestRank",
            "lexicalHit", "lexicalBestRank",
            "fusionHit", "fusionBestRank",
            "rerankHit", "rerankBestRank",
            "selectedDocumentHit", "finalContextHit", "durationMs"));

        foreach (var r in summary.Results)
        {
            sb.AppendLine(string.Join(',',
                Csv(r.CaseId), Csv(r.CaseGroupId), Csv(r.VariantId), Csv(r.QuestionType), Csv(r.RetrievalType),
                Csv(r.Difficulty), Csv(r.Answerability), r.CountedTowardRecall, r.Passed,
                Csv(r.FailureStage.ToWireString()),
                r.Semantic.Hit, Rank(r.Semantic),
                r.Lexical.Hit, Rank(r.Lexical),
                r.Fusion.Hit, Rank(r.Fusion),
                r.Rerank.Hit, Rank(r.Rerank),
                r.SelectedDocument.Hit, r.FinalContext.Hit, r.DurationMs));
        }

        return sb.ToString();
    }

    private static string Rank(RetrievalStageHitInfo info)
        => info.BestRank?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;

    private static string Csv(string? value)
    {
        value ??= string.Empty;
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        return value;
    }
}

