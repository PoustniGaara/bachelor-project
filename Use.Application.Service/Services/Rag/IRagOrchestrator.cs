using Use.Application.Service.Models.Requests;
using Use.Application.Service.Models.Responses;
using Use.Application.Service.Models.Retrieval;

namespace Use.Application.Service.Services.Rag;

/// <summary>
/// Coordinates the full Retrieval-Augmented Generation flow:
/// embed → vector search → prompt build → LLM generation → assemble response.
/// </summary>
public interface IRagOrchestrator
{
    /// <summary>
    /// Run the pipeline and return just the public answer payload. Kept for
    /// backwards-compatible / simple callers.
    /// </summary>
    Task<ChatResponse> AnswerAsync(ChatRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Run the pipeline and return the answer plus retrieval telemetry
    /// (candidate chunks, timings, status) for persistence / logging.
    /// </summary>
    Task<RagExecutionResult> ExecuteAsync(string question, CancellationToken cancellationToken);
}

