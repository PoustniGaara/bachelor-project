using Npgsql;

namespace Use.Application.Service.Services.Persistence;

/// <summary>
/// Owns the single, application-wide <see cref="NpgsqlDataSource"/> (a pooled
/// connection factory) for the PostgreSQL metadata database. Both the lexical
/// search service and the chat-history / RAG-logging repositories share this one
/// pool instead of each building their own — there is a single
/// <c>Postgres:ConnectionString</c> for the whole service.
///
/// <para>
/// Registered as a <b>singleton</b>; it implements <see cref="IAsyncDisposable"/>
/// so the pool is closed cleanly on host shutdown. When Postgres is disabled (or
/// the connection string is empty) <see cref="Enabled"/> is <c>false</c> and the
/// data source is never created — callers degrade gracefully.
/// </para>
/// </summary>
public interface IPostgresDataSourceProvider
{
    /// <summary>True when a PostgreSQL connection is configured and usable.</summary>
    bool Enabled { get; }

    /// <summary>The shared data source, or <c>null</c> when Postgres is disabled.</summary>
    NpgsqlDataSource? DataSource { get; }

    /// <summary>Returns the data source or throws when Postgres is disabled/misconfigured.</summary>
    NpgsqlDataSource RequireDataSource();
}

