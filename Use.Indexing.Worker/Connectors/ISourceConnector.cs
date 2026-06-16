using Use.Indexing.Worker.Models;

namespace Use.Indexing.Worker.Connectors;

/// <summary>
/// Abstraction over a documentation source system. Each source (Wiki.js,
/// Azure DevOps Wiki, SharePoint, ...) gets its own implementation. Adding a
/// new source = adding a new connector + DI registration; orchestration code
/// does not change.
/// </summary>
public interface ISourceConnector
{
    /// <summary>Identifies which source system this connector serves.</summary>
    SourceSystemType SourceSystem { get; }

    /// <summary>Stable, human-readable name for logs/diagnostics.</summary>
    string Name { get; }

    /// <summary>
    /// Discover documents available for indexing. If <paramref name="changedSince"/>
    /// is provided AND the connector supports it, only changed documents should
    /// be returned (incremental indexing). Otherwise it returns the full set.
    /// </summary>
    IAsyncEnumerable<SourceDocumentReference> DiscoverAsync(
        DateTimeOffset? changedSince,
        CancellationToken cancellationToken);

    /// <summary>Fetch the full document content for a previously discovered reference.</summary>
    Task<SourceDocument> FetchAsync(
        SourceDocumentReference reference,
        CancellationToken cancellationToken);
}

