namespace Use.Indexing.Worker.Models;

/// <summary>
/// Result of parsing + normalization: plain text ready for chunking, with
/// preserved metadata (source system, ids, title, url, timestamps, tags, and
/// optional permission hints used later for role-based access filtering).
/// </summary>
public sealed record NormalizedDocument(
    SourceDocumentReference Reference,
    string Title,
    string PlainText,
    IReadOnlyList<string> Tags,
    IReadOnlyDictionary<string, string> Metadata,
    DocumentPermissions? Permissions = null,
    DocumentOutline? Outline = null);

/// <summary>
/// Optional permission descriptor copied from the source system. The indexer
/// itself does not enforce access — it persists this so the retrieval service
/// can filter by caller identity.
/// </summary>
public sealed record DocumentPermissions(
    IReadOnlyList<string> AllowedPrincipals,
    IReadOnlyList<string> DeniedPrincipals);

