using Use.Application.Service.Models.Retrieval;

namespace Use.Application.Service.Services.Rag.ContextAssembly;

/// <summary>
/// For each selected document, loads all its chunks from the vector store and
/// returns them sorted by <see cref="RetrievedChunk.ChunkOrder"/>.
/// </summary>
public interface IContextAssembler
{
    Task<IReadOnlyList<DocumentContext>> AssembleAsync(
        IReadOnlyList<DocumentSelection.DocumentSelection> selections,
        CancellationToken cancellationToken);
}