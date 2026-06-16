using System.Diagnostics;
using System.Globalization;
using System.Text;
using Microsoft.Extensions.Options;
using Use.Application.Service.Configuration;
using Use.Application.Service.Evaluation.Models;

namespace Use.Application.Service.Evaluation;

/// <summary>
/// Drives one retrieval evaluation run: load cases → probe retrieval per case →
/// compute metrics → aggregate → write report → print a compact console summary.
/// Never calls prompt building or LLM generation.
/// </summary>
public sealed class RagEvaluationRunner
{
    private readonly IRetrievalProbe _probe;
    private readonly IRagEvaluationCaseLoader _loader;
    private readonly IRetrievalEvaluator _evaluator;
    private readonly IRagEvaluationReportWriter _reportWriter;
    private readonly RagEvaluationOptions _options;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<RagEvaluationRunner> _logger;

    public RagEvaluationRunner(
        IRetrievalProbe probe,
        IRagEvaluationCaseLoader loader,
        IRetrievalEvaluator evaluator,
        IRagEvaluationReportWriter reportWriter,
        IOptions<RagEvaluationOptions> options,
        IHostEnvironment environment,
        ILogger<RagEvaluationRunner> logger)
    {
        _probe = probe;
        _loader = loader;
        _evaluator = evaluator;
        _reportWriter = reportWriter;
        _options = options.Value;
        _environment = environment;
        _logger = logger;
    }

    public async Task<RagEvaluationSummary> RunAsync(CancellationToken cancellationToken)
    {
        var runId = Guid.NewGuid().ToString("N")[..12];
        var startedAt = DateTimeOffset.Now;
        var stopwatch = Stopwatch.StartNew();

        var casesPath = ResolvePath(_options.CasesPath);
        var outputDir = ResolvePath(_options.OutputDirectory);

        _logger.LogInformation("===== RAG retrieval evaluation started (runId={RunId}) =====", runId);
        _logger.LogInformation("Dataset: {Path}", casesPath);

        var allCases = await _loader.LoadAsync(casesPath, cancellationToken).ConfigureAwait(false);

        var cases = _options.MaxCases is > 0
            ? allCases.Take(_options.MaxCases.Value).ToList()
            : allCases.ToList();

        _logger.LogInformation("Loaded {N} case(s); evaluating {M}.", allCases.Count, cases.Count);

        var results = new List<RagEvaluationResult>(cases.Count);
        for (var i = 0; i < cases.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var c = cases[i];

            _logger.LogInformation("Running case {Index}/{Total} [{CaseId}] {Question}",
                i + 1, cases.Count, c.CaseId, Truncate(c.Question, 80));

            RagEvaluationResult result;
            var caseSw = Stopwatch.StartNew();
            try
            {
                var probe = await _probe.ProbeRetrievalAsync(c.Question, cancellationToken).ConfigureAwait(false);
                result = _evaluator.Evaluate(c, probe);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Case [{CaseId}] failed to evaluate.", c.CaseId);
                result = RetrievalEvaluator.Errored(c, ex.Message);
            }
            caseSw.Stop();
            result.DurationMs = (int)caseSw.ElapsedMilliseconds;
            results.Add(result);

            LogCaseOutcome(i + 1, cases.Count, result);
        }

        stopwatch.Stop();
        var summary = BuildSummary(runId, startedAt, stopwatch.ElapsedMilliseconds, allCases.Count, results);

        var reportPath = await _reportWriter.WriteAsync(summary, outputDir, cancellationToken).ConfigureAwait(false);
        PrintConsoleSummary(summary, reportPath);

        _logger.LogInformation("===== RAG retrieval evaluation finished (runId={RunId}) =====", runId);
        return summary;
    }

    private void LogCaseOutcome(int index, int total, RagEvaluationResult r)
    {
        if (!r.CountedTowardRecall)
        {
            _logger.LogInformation("  case {Index}/{Total} [{CaseId}] informational ({Stage}).",
                index, total, r.CaseId, r.FailureStage.ToWireString());
            return;
        }

        if (r.Passed)
            _logger.LogInformation("  case {Index}/{Total} [{CaseId}] PASSED (finalRank={Rank}).",
                index, total, r.CaseId, r.FinalContext.BestRank);
        else
            _logger.LogWarning("  case {Index}/{Total} [{CaseId}] FAILED ({Stage}).",
                index, total, r.CaseId, r.FailureStage.ToWireString());
    }

    private RagEvaluationSummary BuildSummary(
        string runId, DateTimeOffset startedAt, long durationMs, int totalCases,
        IReadOnlyList<RagEvaluationResult> results)
    {
        var counted = results.Where(r => r.CountedTowardRecall).ToList();
        var passed = counted.Count(r => r.Passed);

        var summary = new RagEvaluationSummary
        {
            RunId = runId,
            StartedAt = startedAt,
            FinishedAt = startedAt.AddMilliseconds(durationMs),
            DurationMs = durationMs,
            TotalCases = totalCases,
            EvaluatedCases = results.Count,
            SkippedCases = totalCases - results.Count,
            CountedCases = counted.Count,
            PassedCases = passed,
            FailedCases = counted.Count - passed,
            FinalContextRecall = Recall(counted, r => r.FinalContext),
            SelectedDocumentRecall = Recall(counted, r => r.SelectedDocument),
            FusionRecall = Recall(counted, r => r.Fusion),
            RerankRecall = Recall(counted, r => r.Rerank),
            Results = results.ToList()
        };

        foreach (var r in results)
        {
            var key = r.FailureStage.ToWireString();
            summary.FailureStageCounts[key] = summary.FailureStageCounts.GetValueOrDefault(key) + 1;
        }

        summary.MetricsByQuestionType = BucketBy(results, r => r.QuestionType);
        summary.MetricsByDifficulty = BucketBy(results, r => r.Difficulty);
        return summary;
    }

    private static Dictionary<string, MetricBucket> BucketBy(
        IReadOnlyList<RagEvaluationResult> results, Func<RagEvaluationResult, string?> keySelector)
    {
        return results
            .GroupBy(r => keySelector(r) ?? "unspecified", StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g =>
            {
                var counted = g.Where(r => r.CountedTowardRecall).ToList();
                return new MetricBucket
                {
                    TotalCases = g.Count(),
                    CountedCases = counted.Count,
                    PassedCases = counted.Count(r => r.Passed),
                    FinalContextRecall = Recall(counted, r => r.FinalContext),
                    SelectedDocumentRecall = Recall(counted, r => r.SelectedDocument),
                    FusionRecall = Recall(counted, r => r.Fusion),
                    RerankRecall = Recall(counted, r => r.Rerank)
                };
            }, StringComparer.Ordinal);
    }

    // Recall over cases where the stage is applicable: hits / applicable.
    private static double Recall(
        IReadOnlyList<RagEvaluationResult> counted, Func<RagEvaluationResult, RetrievalStageHitInfo> stage)
    {
        var applicable = counted.Where(r => stage(r).Applicable).ToList();
        if (applicable.Count == 0) return 0d;
        var hits = applicable.Count(r => stage(r).Hit);
        return (double)hits / applicable.Count;
    }

    private void PrintConsoleSummary(RagEvaluationSummary s, string reportPath)
    {
        var sb = new StringBuilder();
        sb.AppendLine();
        sb.AppendLine("RAG Evaluation finished");
        sb.AppendLine(Inv($"Cases evaluated: {s.EvaluatedCases} (counted toward recall: {s.CountedCases})"));
        sb.AppendLine(Inv($"Passed: {s.PassedCases}   Failed: {s.FailedCases}"));
        sb.AppendLine(Inv($"Final context recall: {Pct(s.FinalContextRecall)}"));
        sb.AppendLine(Inv($"Selected document recall: {Pct(s.SelectedDocumentRecall)}"));
        sb.AppendLine(Inv($"Fusion recall: {Pct(s.FusionRecall)}"));
        sb.AppendLine(Inv($"Rerank recall: {Pct(s.RerankRecall)}"));
        sb.AppendLine();
        sb.AppendLine("Failure stages:");
        foreach (var kv in s.FailureStageCounts.OrderByDescending(k => k.Value))
            sb.AppendLine(Inv($"  {kv.Key}: {kv.Value}"));
        sb.AppendLine();
        sb.AppendLine("Report written to:");
        sb.AppendLine($"  {reportPath}");

        Console.WriteLine(sb.ToString());
        _logger.LogInformation("{Summary}", sb.ToString());
    }

    private string ResolvePath(string path)
        => Path.IsPathRooted(path) ? path : Path.Combine(_environment.ContentRootPath, path);

    private static string Pct(double ratio) => Inv($"{ratio * 100d:0.0}%");

    private static string Inv(FormattableString s) => s.ToString(CultureInfo.InvariantCulture);

    private static string Truncate(string text, int max)
        => text.Length <= max ? text : text[..max] + "…";
}

