namespace Use.Application.Service.Evaluation;

/// <summary>
/// Runs the RAG pipeline in <b>retrieval-only</b> mode: embedding → hybrid
/// retrieval → RRF fusion → reranking → document selection → context assembly,
/// stopping <em>before</em> prompt building and LLM generation.
///
/// <para>
/// Implemented by <c>RagOrchestrator</c> so the evaluation harness exercises the
/// exact same services and behaviour as the normal chat flow, without ever
/// calling the final generation step.
/// </para>
/// </summary>
public interface IRetrievalProbe
{
    Task<RetrievalProbeResult> ProbeRetrievalAsync(string question, CancellationToken cancellationToken);
}

