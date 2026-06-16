namespace Use.Application.Service.Configuration;

/// <summary>
/// Configuration for the optional automated <b>retrieval evaluation</b> harness.
/// Bound from the "Evaluation" section of appsettings.json.
///
/// <para>
/// The whole feature is inert unless <see cref="EvaluationModeEnabled"/> is
/// <c>true</c>. When disabled, the Application Service behaves exactly like the
/// normal RAG chat service — no evaluation code runs.
/// </para>
/// </summary>
public sealed class RagEvaluationOptions
{
    public const string SectionName = "Evaluation";

    /// <summary>Master switch. When false the evaluation feature does nothing.</summary>
    public bool EvaluationModeEnabled { get; set; } = false;

    /// <summary>
    /// Path to the JSONL evaluation dataset. Relative paths are resolved against
    /// the application content root (the project directory in Development).
    /// </summary>
    public string CasesPath { get; set; } = "Evaluation/rag_evaluation_cases.v1.jsonl";

    /// <summary>
    /// Directory the JSON/CSV reports are written to. Relative paths are resolved
    /// against the application content root. Created if it does not exist.
    /// </summary>
    public string OutputDirectory { get; set; } = "Evaluation/Reports";

    /// <summary>When true (and <see cref="EvaluationModeEnabled"/> is true) the
    /// evaluation runs automatically on application startup.</summary>
    public bool RunOnStartup { get; set; } = true;

    /// <summary>Optional cap on how many enabled cases to evaluate. Null = all.</summary>
    public int? MaxCases { get; set; }

    /// <summary>When true the host shuts down after the evaluation completes
    /// (useful for one-shot CI / batch runs).</summary>
    public bool StopApplicationAfterEvaluation { get; set; } = false;
}

