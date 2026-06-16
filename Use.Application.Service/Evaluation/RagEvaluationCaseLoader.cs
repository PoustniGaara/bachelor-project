using System.Text.Json;
using Use.Application.Service.Evaluation.Models;

namespace Use.Application.Service.Evaluation;

/// <summary>
/// Default <see cref="IRagEvaluationCaseLoader"/>. Parses one JSON object per
/// non-empty line with <see cref="System.Text.Json"/>.
/// </summary>
public sealed class RagEvaluationCaseLoader : IRagEvaluationCaseLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private readonly ILogger<RagEvaluationCaseLoader> _logger;

    public RagEvaluationCaseLoader(ILogger<RagEvaluationCaseLoader> logger) => _logger = logger;

    public async Task<IReadOnlyList<RagEvaluationCase>> LoadAsync(
        string absolutePath, CancellationToken cancellationToken)
    {
        if (!File.Exists(absolutePath))
            throw new FileNotFoundException(
                $"Evaluation dataset not found at '{absolutePath}'. " +
                "Set Evaluation:CasesPath or place the JSONL file there.", absolutePath);

        var valid = new List<RagEvaluationCase>();
        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        var lineNumber = 0;
        var invalidLines = 0;
        var disabled = 0;

        using var reader = new StreamReader(absolutePath);
        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } rawLine)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lineNumber++;

            var line = rawLine.Trim();
            if (line.Length == 0) continue;                 // ignore empty lines
            if (line.StartsWith("//", StringComparison.Ordinal)) continue; // tolerate comment lines

            RagEvaluationCase? c;
            try
            {
                c = JsonSerializer.Deserialize<RagEvaluationCase>(line, JsonOptions);
            }
            catch (JsonException ex)
            {
                invalidLines++;
                _logger.LogWarning("Skipping invalid JSONL line {Line}: {Message}", lineNumber, ex.Message);
                continue;
            }

            if (c is null)
            {
                invalidLines++;
                _logger.LogWarning("Skipping null JSONL case at line {Line}.", lineNumber);
                continue;
            }

            if (!c.Enabled)
            {
                disabled++;
                continue;                                   // ignore disabled cases
            }

            if (string.IsNullOrWhiteSpace(c.CaseId))
            {
                invalidLines++;
                _logger.LogWarning("Skipping case at line {Line}: missing 'caseId'.", lineNumber);
                continue;
            }

            if (string.IsNullOrWhiteSpace(c.Question))
            {
                invalidLines++;
                _logger.LogWarning("Skipping case '{CaseId}' (line {Line}): missing 'question'.", c.CaseId, lineNumber);
                continue;
            }

            if (!seenIds.Add(c.CaseId))
                _logger.LogWarning("Duplicate caseId '{CaseId}' at line {Line} — keeping both.", c.CaseId, lineNumber);

            valid.Add(c);
        }

        _logger.LogInformation(
            "Loaded {Valid} enabled evaluation case(s) from '{Path}' ({Disabled} disabled, {Invalid} invalid).",
            valid.Count, absolutePath, disabled, invalidLines);

        if (valid.Count == 0)
            throw new InvalidOperationException(
                $"No valid, enabled evaluation cases found in '{absolutePath}'.");

        return valid;
    }
}

