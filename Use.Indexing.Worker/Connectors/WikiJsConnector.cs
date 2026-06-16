using System.Runtime.CompilerServices;
using Microsoft.Extensions.Options;
using Use.Indexing.Worker.Configuration;
using Use.Indexing.Worker.Connectors.WikiJs;
using Use.Indexing.Worker.Models;

namespace Use.Indexing.Worker.Connectors;

/// <summary>
/// Real Wiki.js connector. Uses <see cref="WikiJsGraphQlClient"/> to:
///   1. Discover: list all pages (light metadata, ordered by UPDATED DESC).
///   2. Fetch:    load full Markdown content for one page by id.
/// The Markdown body is returned as a <see cref="SourceDocument"/> with
/// content type "text/markdown"; normalization happens later in the pipeline.
/// </summary>
public sealed class WikiJsConnector : ISourceConnector
{
    private readonly WikiJsGraphQlClient _client;
    private readonly WikiJsOptions _options;
    private readonly ILogger<WikiJsConnector> _logger;

    public WikiJsConnector(
        WikiJsGraphQlClient client,
        IOptions<IndexingOptions> options,
        ILogger<WikiJsConnector> logger)
    {
        _client = client;
        _options = options.Value.WikiJs;
        _logger = logger;
    }

    public SourceSystemType SourceSystem => SourceSystemType.WikiJs;
    public string Name => "Wiki.js";

    public async IAsyncEnumerable<SourceDocumentReference> DiscoverAsync(
        DateTimeOffset? changedSince,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Wiki.js connector disabled by configuration.");
            yield break;
        }

        _logger.LogInformation(
            "Wiki.js: discovering pages (changedSince={ChangedSince}, onlyPublished={OnlyPublished}, skipPrivate={SkipPrivate})",
            changedSince, _options.OnlyPublished, _options.SkipPrivate);

        // NOTE: Wiki.js's pages.list query does not support a server-side
        // "changed since" filter, so we filter client-side by updatedAt.
        // For very large wikis, an incremental cursor could be tracked in
        // IIndexRepository instead.
        var pages = await _client.ListPagesAsync(cancellationToken);
        _logger.LogInformation("Wiki.js: {Count} pages returned by list query", pages.Count);

        foreach (var page in pages)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (_options.OnlyPublished && !page.IsPublished) continue;
            if (_options.SkipPrivate && page.IsPrivate) continue;
            if (changedSince.HasValue && page.UpdatedAt is { } u && u <= changedSince.Value) continue;

            yield return new SourceDocumentReference(
                SourceSystem: SourceSystemType.WikiJs,
                SourceDocumentId: page.Id.ToString(),
                Title: page.Title,
                Url: BuildPageUrl(page.Path, page.Locale),
                LastModified: page.UpdatedAt);
        }
    }

    public async Task<SourceDocument> FetchAsync(
        SourceDocumentReference reference, CancellationToken cancellationToken)
    {
        if (!int.TryParse(reference.SourceDocumentId, out var id))
            throw new InvalidOperationException(
                $"Wiki.js page id must be an integer; got '{reference.SourceDocumentId}'.");

        var page = await _client.GetPageAsync(id, cancellationToken)
                   ?? throw new InvalidOperationException($"Wiki.js page {id} not found.");

        // Pure Markdown is preferred for the indexing pipeline. Render (HTML)
        // is preserved as a metadata flag in case a future stage wants it.
        var body = page.Content ?? string.Empty;

        var meta = new Dictionary<string, string>
        {
            ["sourceSystem"]     = SourceSystemType.WikiJs.ToString(),
            ["sourceDocumentId"] = page.Id.ToString(),
            ["path"]             = page.Path,
            ["url"]              = reference.Url,
            ["locale"]           = page.Locale ?? string.Empty,
            ["description"]      = page.Description ?? string.Empty,
            ["createdAt"]        = page.CreatedAt?.ToString("O") ?? string.Empty,
            ["updatedAt"]        = page.UpdatedAt?.ToString("O") ?? string.Empty,
            ["hasRender"]        = (!string.IsNullOrEmpty(page.Render)).ToString()
        };

        return new SourceDocument(
            Reference: reference with
            {
                Title = string.IsNullOrWhiteSpace(page.Title) ? reference.Title : page.Title,
                LastModified = page.UpdatedAt ?? reference.LastModified
            },
            ContentType: "text/markdown",
            RawContent: body,
            Metadata: meta);
    }

    private string BuildPageUrl(string path, string? locale)
    {
        var baseUrl = _options.BaseUrl.TrimEnd('/');
        return string.IsNullOrWhiteSpace(locale)
            ? $"{baseUrl}/{path.TrimStart('/')}"
            : $"{baseUrl}/{locale}/{path.TrimStart('/')}";
    }
}
