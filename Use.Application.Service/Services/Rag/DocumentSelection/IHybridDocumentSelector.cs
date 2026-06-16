using Use.Application.Service.Models.Retrieval;

namespace Use.Application.Service.Services.Rag.DocumentSelection;

/// <summary>
/// Picks the most relevant source documents out of a fused (RRF) chunk set.
/// Unlike the legacy semantic-only <see cref="IDocumentSelector"/> (which ranks
/// purely by chunk-hit count), this selector scores documents from the fused
/// chunk scores so a page with a single, strongly relevant lexical hit can win.
/// </summary>
public interface IHybridDocumentSelector
{
    IReadOnlyList<DocumentSelection> Select(
        IReadOnlyList<FusedChunkResult> fusedChunks,
        int topDocuments);
}

