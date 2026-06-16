namespace Use.Indexing.Worker.Models;

/// <summary>
/// Identifies the source system a document originated from. Stored alongside
/// every document/chunk so retrieval can show traceable provenance and
/// future role/source-based filtering can apply.
/// </summary>
public enum SourceSystemType
{
    Unknown = 0,
    WikiJs = 1,
    AzureDevOpsWiki = 2,
    SharePoint = 3
}

