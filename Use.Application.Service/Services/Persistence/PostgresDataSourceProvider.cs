using Microsoft.Extensions.Options;
using Npgsql;
using Use.Application.Service.Configuration;

namespace Use.Application.Service.Services.Persistence;

/// <inheritdoc cref="IPostgresDataSourceProvider"/>
public sealed class PostgresDataSourceProvider : IPostgresDataSourceProvider, IAsyncDisposable
{
    private readonly NpgsqlDataSource? _dataSource;
    private readonly ILogger<PostgresDataSourceProvider> _logger;

    public PostgresDataSourceProvider(
        IOptions<PostgresOptions> options,
        ILogger<PostgresDataSourceProvider> logger)
    {
        _logger = logger;
        var o = options.Value;

        if (!o.Enabled)
        {
            _logger.LogInformation("PostgreSQL disabled by configuration — chat history / RAG logging are no-ops.");
            return;
        }

        if (string.IsNullOrWhiteSpace(o.ConnectionString))
        {
            _logger.LogError(
                "PostgreSQL is enabled but Postgres:ConnectionString is empty. " +
                "Set it via appsettings.json, user-secrets, or the Postgres__ConnectionString environment variable.");
            return;
        }

        _dataSource = new NpgsqlDataSourceBuilder(o.ConnectionString).Build();
        _logger.LogInformation("PostgreSQL data source initialised (shared pool for lexical search + chat history).");
    }

    public bool Enabled => _dataSource is not null;

    public NpgsqlDataSource? DataSource => _dataSource;

    public NpgsqlDataSource RequireDataSource()
        => _dataSource ?? throw new InvalidOperationException(
            "PostgreSQL is not configured (Postgres:Enabled=false or missing/invalid Postgres:ConnectionString).");

    public async ValueTask DisposeAsync()
    {
        if (_dataSource is not null)
            await _dataSource.DisposeAsync().ConfigureAwait(false);
    }
}

