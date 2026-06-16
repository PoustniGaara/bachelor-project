using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using Use.Indexing.Worker.Configuration;

namespace Use.Indexing.Worker.Connectors.WikiJs;

/// <summary>
/// Thin GraphQL client for Wiki.js. Only knows how to talk to the two
/// endpoints we need:
///  - pages.list: lightweight metadata for discovery
///  - pages.single(id): full content + render for a specific page
/// Authentication uses a Bearer token from <see cref="WikiJsOptions.AccessToken"/>.
/// </summary>
public sealed class WikiJsGraphQlClient
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _http;
    private readonly WikiJsOptions _options;
    private readonly ILogger<WikiJsGraphQlClient> _logger;

    public WikiJsGraphQlClient(
        HttpClient http,
        IOptions<IndexingOptions> options,
        ILogger<WikiJsGraphQlClient> logger)
    {
        _http = http;
        _options = options.Value.WikiJs;
        _logger = logger;

        _http.BaseAddress = new Uri(_options.BaseUrl.TrimEnd('/') + "/");

        if (!string.IsNullOrWhiteSpace(_options.AccessToken))
        {
            _http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", _options.AccessToken);
        }
        else
        {
            _logger.LogWarning("Wiki.js access token is not configured — requests will likely fail.");
        }

        _http.DefaultRequestHeaders.Accept.Clear();
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    /// <summary>Lists all pages with light metadata, ordered by UPDATED DESC.</summary>
    public async Task<IReadOnlyList<WikiJsPage>> ListPagesAsync(CancellationToken cancellationToken)
    {
        const string query = """
        {
          pages {
            list(orderBy: UPDATED, orderByDirection: DESC) {
              id
              path
              title
              description
              locale
              tags
              isPublished
              isPrivate
              updatedAt
              createdAt
            }
          }
        }
        """;

        var response = await PostAsync<PagesListResponse>(query, variables: null, cancellationToken);
        var list = response?.Pages?.List;
        return list is null ? Array.Empty<WikiJsPage>() : list;
    }

    /// <summary>Fetches full content + render for a single page.</summary>
    public async Task<WikiJsPage?> GetPageAsync(int id, CancellationToken cancellationToken)
    {
        const string query = """
        query GetPage($id: Int!) {
          pages {
            single(id: $id) {
              id
              path
              title
              description
              content
              render
              createdAt
              updatedAt
            }
          }
        }
        """;

        var response = await PostAsync<PagesSingleResponse>(
            query, new Dictionary<string, object?> { ["id"] = id }, cancellationToken);

        return response?.Pages?.Single;
    }

    private async Task<TData?> PostAsync<TData>(
        string query,
        IReadOnlyDictionary<string, object?>? variables,
        CancellationToken cancellationToken) where TData : class
    {
        var endpoint = _options.GraphQlEndpoint.TrimStart('/');
        var payload = new GraphQlRequest(query, variables);

        using var req = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = JsonContent.Create(payload, options: JsonOpts)
        };

        using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        resp.EnsureSuccessStatusCode();

        var envelope = await resp.Content.ReadFromJsonAsync<GraphQlResponse<TData>>(JsonOpts, cancellationToken);

        if (envelope?.Errors is { Count: > 0 } errors)
        {
            // Don't dump variables (could contain ids/secrets in other contexts).
            var first = errors[0].Message ?? "unknown";
            _logger.LogError("Wiki.js GraphQL returned {Count} error(s); first: {Message}", errors.Count, first);
            throw new InvalidOperationException($"Wiki.js GraphQL error: {first}");
        }

        return envelope?.Data;
    }

    // ---- DTOs --------------------------------------------------------------

    private sealed record GraphQlRequest(
        [property: JsonPropertyName("query")] string Query,
        [property: JsonPropertyName("variables")] IReadOnlyDictionary<string, object?>? Variables);

    private sealed class GraphQlResponse<TData>
    {
        public TData? Data { get; set; }
        public List<GraphQlError>? Errors { get; set; }
    }

    private sealed class GraphQlError
    {
        public string? Message { get; set; }
    }

    private sealed class PagesListResponse
    {
        public PagesListInner? Pages { get; set; }
    }

    private sealed class PagesListInner
    {
        public List<WikiJsPage>? List { get; set; }
    }

    private sealed class PagesSingleResponse
    {
        public PagesSingleInner? Pages { get; set; }
    }

    private sealed class PagesSingleInner
    {
        public WikiJsPage? Single { get; set; }
    }
}

