namespace Use.Indexing.Worker.Configuration;

/// <summary>
/// Top-level options for the indexing worker, bound from the "Indexing"
/// section of appsettings.json (and overridable via environment variables).
/// </summary>
public sealed class IndexingOptions
{
    public const string SectionName = "Indexing";

    /// <summary>Interval between scheduled indexing cycles.</summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>Delay before the first indexing cycle after startup.</summary>
    public TimeSpan StartupDelay { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// If true, the worker performs a full re-index on each cycle.
    /// If false, the orchestrator will use connector-provided "since" cursors
    /// to perform incremental indexing where supported.
    /// </summary>
    public bool ForceFullReindex { get; set; } = false;

    /// <summary>Per-cycle safety cap on number of documents processed.</summary>
    public int MaxDocumentsPerCycle { get; set; } = 1000;

    public ChunkingOptions Chunking { get; set; } = new();
    public EmbeddingOptions Embedding { get; set; } = new();
    public WikiJsOptions WikiJs { get; set; } = new();
    public LlmServiceOptions LlmService { get; set; } = new();
    public QdrantOptions Qdrant { get; set; } = new();
    public PostgresOptions Postgres { get; set; } = new();
    public ChunkDumpOptions ChunkDump { get; set; } = new();
}

/// <summary>
/// Connection settings for the PostgreSQL metadata store. PostgreSQL is the
/// lexical / BM25-ready chunk store (full-text search via tsvector), kept
/// independent of the Qdrant vector store. The same deterministic chunk id is
/// persisted to both systems.
///
/// <para>
/// The connection string normally lives in appsettings.Development.json or
/// user-secrets (it carries a password). It can also be supplied via the
/// standard environment-variable convention:
///   Indexing__Postgres__ConnectionString
///   Indexing__Postgres__Enabled
/// </para>
/// </summary>
public sealed class PostgresOptions
{
    /// <summary>Master switch. When false the worker behaves exactly as before
    /// (Qdrant only) and no PostgreSQL connection is attempted.</summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// Npgsql connection string, e.g.
    /// "Host=localhost;Port=5432;Database=use_metadata_db;Username=use_app_user;Password=...".
    /// </summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// If true, the repository runs idempotent CREATE TABLE/INDEX statements on
    /// first use. Convenient for a local prototype; disable in environments
    /// where the schema is managed by migrations.
    /// </summary>
    public bool EnsureSchema { get; set; } = true;
}

/// <summary>
/// Diagnostic dump of normalized + chunked text to disk, exactly as it would
/// be sent to the embedding model. Useful for inspecting chunk quality before
/// committing to a vector DB schema. Should be turned off in production.
/// </summary>
public sealed class ChunkDumpOptions
{
    /// <summary>Master switch for the dump feature.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Output root directory. Relative paths resolve against the app
    /// working directory. A per-source subfolder is created automatically.</summary>
    public string OutputDirectory { get; set; } = "chunk-dumps";

    /// <summary>If true, the per-source folder is wiped at the start of each
    /// indexing cycle so it always reflects the latest run.</summary>
    public bool CleanOnStart { get; set; } = true;

    /// <summary>If true, also writes a single `_full.txt` containing the
    /// normalized plain text of the document before chunking.</summary>
    public bool IncludeFullDocument { get; set; } = true;
}

public sealed class ChunkingOptions
{
    /// <summary>Soft upper bound on characters per chunk; structure-aware splitter
    /// will never exceed this except when a single indivisible block is larger.</summary>
    public int TargetCharacters { get; set; } = 1200;

    /// <summary>Below this size, a section is buffered and merged with its
    /// next sibling to avoid emitting tiny, low-signal chunks.</summary>
    public int MinCharacters { get; set; } = 300;

    /// <summary>Number of trailing characters re-emitted at the start of the
    /// next chunk when a section is split. Provides retrieval context bridging.</summary>
    public int Overlap { get; set; } = 150;

    /// <summary>Sections deeper than this collapse into their nearest ancestor
    /// instead of being emitted as standalone chunks.</summary>
    public int MaxHeadingDepth { get; set; } = 4;
}

public sealed class EmbeddingOptions
{
    /// <summary>Logical provider key (e.g. "stub", "openai", "azure-openai").</summary>
    public string Provider { get; set; } = "stub";
    public string Model { get; set; } = "text-embedding-3-small";
    public int Dimensions { get; set; } = 1536;
    // TODO: API keys/endpoints belong here, but should be sourced from
    // environment variables / user-secrets / Key Vault — not committed config.
}

public sealed class WikiJsOptions
{
    public bool Enabled { get; set; } = true;
    public string BaseUrl { get; set; } = "http://localhost:3000";
    public string GraphQlEndpoint { get; set; } = "/graphql";

    /// <summary>
    /// Bearer token for Wiki.js GraphQL. Should be supplied via user-secrets,
    /// environment variable (Indexing__WikiJs__AccessToken), or appsettings.Development.json
    /// during prototyping — NOT committed to source control.
    /// </summary>
    public string? AccessToken { get; set; }

    /// <summary>If true, only published pages are indexed.</summary>
    public bool OnlyPublished { get; set; } = true;

    /// <summary>If true, private pages are skipped.</summary>
    public bool SkipPrivate { get; set; } = true;

    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(30);
}

/// <summary>
/// Connection settings for the Use.LlmService.Api which produces embeddings
/// (and chat completions) by delegating to a local Ollama-hosted model.
/// </summary>
public sealed class LlmServiceOptions
{
    /// <summary>Base URL of the LLM service, e.g. "http://localhost:5133".</summary>
    public string BaseUrl { get; set; } = "http://localhost:5133";

    /// <summary>Total HTTP timeout for a single embedding request.</summary>
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>Maximum embedding requests issued concurrently. The local
    /// Ollama runtime is largely single-threaded, so the default is small.</summary>
    public int MaxParallelism { get; set; } = 4;

    /// <summary>Number of retries on transient HTTP failures.</summary>
    public int MaxRetries { get; set; } = 3;
}

/// <summary>
/// Connection + collection settings for the Qdrant vector database.
/// Port 6334 is the gRPC endpoint (preferred by Qdrant.Client).
/// </summary>
public sealed class QdrantOptions
{
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 6334;
    public bool UseHttps { get; set; } = false;
    public string? ApiKey { get; set; }

    /// <summary>Logical collection that holds all chunk vectors.</summary>
    public string CollectionName { get; set; } = "documentation_chunks";

    /// <summary>Vector dimensionality. Must match the embedding model output.</summary>
    public int VectorSize { get; set; } = 768;

    /// <summary>Distance metric used by the collection ("Cosine", "Dot", "Euclid").</summary>
    public string Distance { get; set; } = "Cosine";
}
