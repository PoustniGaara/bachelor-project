namespace Use.Application.Service.Models.Logging;

/// <summary>
/// Allowed values for <c>rag_query_log.answer_status</c>. Must stay in sync with
/// the <c>chk_rag_query_log_answer_status</c> CHECK constraint in <c>create.sql</c>.
/// </summary>
public static class RagAnswerStatus
{
    public const string Completed = "completed";
    public const string Failed = "failed";
    public const string Cancelled = "cancelled";
    public const string NoRelevantContext = "no_relevant_context";
}

