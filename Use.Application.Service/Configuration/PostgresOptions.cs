namespace Use.Application.Service.Configuration;

/// <summary>
/// Connection settings for the PostgreSQL metadata store. PostgreSQL is the
/// lexical / BM25-ready chunk store (full-text search via tsvector) written by
/// <c>Use.Indexing.Worker</c>. This service only reads from it for lexical
/// retrieval — Qdrant remains the source for vector search and full-document
/// context expansion.
///
/// <para>
/// Bound from the "Postgres" section of appsettings.json and overridable with
/// the standard environment-variable convention:
///   Postgres__Enabled
///   Postgres__ConnectionString
/// The connection string carries a password and in real deployments should
/// come from environment variables / user-secrets rather than committed config.
/// </para>
/// </summary>
public sealed class PostgresOptions
{
    public const string SectionName = "Postgres";

    /// <summary>Master switch. When false no PostgreSQL connection is attempted
    /// and hybrid/lexical retrieval degrades to semantic-only.</summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// Npgsql connection string, e.g.
    /// "Host=localhost;Port=5432;Database=use_metadata_db;Username=use_app_user;Password=...".
    /// </summary>
    public string ConnectionString { get; set; } = string.Empty;
}

