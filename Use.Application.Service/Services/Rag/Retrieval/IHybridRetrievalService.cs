using Use.Application.Service.Configuration;
using Use.Application.Service.Models.Retrieval;

namespace Use.Application.Service.Services.Rag.Retrieval;

/// <summary>
/// Runs the semantic (Qdrant) and/or lexical (PostgreSQL) retrieval passes and
/// fuses their results with Reciprocal Rank Fusion (RRF). The orchestrator owns
/// the question embedding and the resolved <see cref="RetrievalMode"/>; this
/// service only performs retrieval + fusion.
/// </summary>
public interface IHybridRetrievalService
{
    /// <summary>
    /// Retrieves and fuses candidate chunks.
    /// </summary>
    /// <param name="question">The raw user question (used for lexical search).</param>
    /// <param name="questionEmbedding">
    /// The question embedding for the semantic pass, or <c>null</c> for
    /// <see cref="RetrievalMode.LexicalOnly"/>.
    /// </param>
    /// <param name="mode">The resolved retrieval mode (Hybrid or LexicalOnly).</param>
    /// <param name="cancellationToken">Cancellation token for the retrieval calls.</param>
    Task<IReadOnlyList<FusedChunkResult>> RetrieveAsync(
        string question,
        IReadOnlyList<float>? questionEmbedding,
        RetrievalMode mode,
        CancellationToken cancellationToken);
}

