using Use.Application.Service.Evaluation.Models;

namespace Use.Application.Service.Evaluation;

/// <summary>Loads and validates evaluation cases from a JSONL dataset.</summary>
public interface IRagEvaluationCaseLoader
{
    /// <summary>
    /// Loads enabled, valid cases from <paramref name="absolutePath"/>. Invalid
    /// lines are logged and skipped. Throws only when the file is missing or
    /// contains no usable cases.
    /// </summary>
    Task<IReadOnlyList<RagEvaluationCase>> LoadAsync(string absolutePath, CancellationToken cancellationToken);
}

