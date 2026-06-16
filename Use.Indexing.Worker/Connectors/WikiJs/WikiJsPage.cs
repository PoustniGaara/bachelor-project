namespace Use.Indexing.Worker.Connectors.WikiJs;

/// <summary>
/// Strongly-typed projection of a Wiki.js page. This is the connector's own
/// data shape — kept separate from the generic indexing models so changes in
/// Wiki.js's GraphQL schema only affect this folder.
///
/// Discovery returns "list" entries (no body). The full body is loaded with
/// <see cref="Content"/>/<see cref="Render"/> via the per-page fetch.
/// Normalization of the Markdown body happens later in the pipeline.
/// </summary>
public sealed class WikiJsPage
{
    public int Id { get; set; }
    public string Path { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Locale { get; set; }
    public List<string> Tags { get; set; } = new();
    public bool IsPublished { get; set; }
    public bool IsPrivate { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }

    /// <summary>Raw Markdown body. Only populated by the single-page fetch.</summary>
    public string? Content { get; set; }

    /// <summary>Server-rendered HTML. Available alongside <see cref="Content"/>.</summary>
    public string? Render { get; set; }
}

