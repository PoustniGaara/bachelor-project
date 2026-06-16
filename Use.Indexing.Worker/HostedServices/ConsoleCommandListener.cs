using Microsoft.Extensions.Hosting;
using Use.Indexing.Worker.Models;
using Use.Indexing.Worker.Orchestration;
using Use.Indexing.Worker.Persistence.Postgres;

namespace Use.Indexing.Worker.HostedServices;

/// <summary>
/// Reads commands from STDIN while the worker is running and dispatches them
/// to <see cref="IReindexTriggerHandler"/>. Useful for manually triggering an
/// indexing run during development without waiting for the schedule.
///
/// Supported commands (case-insensitive):
///   index                         → incremental index across all sources
///   index full                    → full reindex across all sources
///   index wikijs                  → incremental, only Wiki.js
///   index wikijs full             → full reindex, only Wiki.js
///   index wikijs &lt;id&gt;             → reindex one Wiki.js page by id
///   search-sql wikijs "query"     → PostgreSQL lexical/BM25 search test
///   help                          → list commands
///   quit | exit                   → stop the host
///
/// If STDIN is not a terminal (e.g. running under systemd), this listener
/// quietly exits and the scheduled worker keeps running normally.
/// </summary>
public sealed class ConsoleCommandListener : BackgroundService
{
    private readonly IReindexTriggerHandler _trigger;
    private readonly ISqlChunkRepository _sqlChunkStore;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly ILogger<ConsoleCommandListener> _logger;

    public ConsoleCommandListener(
        IReindexTriggerHandler trigger,
        ISqlChunkRepository sqlChunkStore,
        IHostApplicationLifetime lifetime,
        ILogger<ConsoleCommandListener> logger)
    {
        _trigger = trigger;
        _sqlChunkStore = sqlChunkStore;
        _lifetime = lifetime;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (Console.IsInputRedirected && !Environment.UserInteractive)
        {
            _logger.LogInformation("STDIN not interactive — console command listener disabled.");
            return;
        }

        PrintHelp();

        // Read on a worker thread so we don't block other startup hosted services.
        await Task.Yield();

        while (!stoppingToken.IsCancellationRequested)
        {
            string? line;
            try
            {
                line = await Task.Run(() => Console.In.ReadLineAsync(stoppingToken).AsTask(), stoppingToken);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Console read failed; stopping listener.");
                break;
            }

            if (line is null) break; // EOF
            line = line.Trim();
            if (line.Length == 0) continue;

            await HandleAsync(line, stoppingToken);
        }
    }

    private async Task HandleAsync(string line, CancellationToken cancellationToken)
    {
        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var cmd = parts[0].ToLowerInvariant();

        switch (cmd)
        {
            case "help" or "?":
                PrintHelp();
                return;

            case "quit" or "exit":
                Console.WriteLine("Stopping worker...");
                _lifetime.StopApplication();
                return;

            case "index":
                await HandleIndexAsync(parts, cancellationToken);
                return;

            case "search-sql" or "lexical-search":
                await HandleLexicalSearchAsync(line, parts, cancellationToken);
                return;

            default:
                Console.WriteLine($"Unknown command: '{cmd}'. Type 'help'.");
                return;
        }
    }

    private async Task HandleIndexAsync(string[] parts, CancellationToken cancellationToken)
    {
        // parts[0] == "index"
        SourceSystemType? source = null;
        bool full = false;
        string? docId = null;

        for (var i = 1; i < parts.Length; i++)
        {
            var t = parts[i].ToLowerInvariant();
            switch (t)
            {
                case "full":
                    full = true;
                    break;
                case "wikijs" or "wiki.js" or "wiki":
                    source = SourceSystemType.WikiJs;
                    break;
                default:
                    // If we already saw a source, treat extra token as a doc id.
                    if (source is not null && docId is null) { docId = parts[i]; }
                    else { Console.WriteLine($"Ignoring unknown token: '{parts[i]}'"); }
                    break;
            }
        }

        var options = new IndexingJobOptions(
            FullReindex: full,
            OnlySource: source,
            OnlySourceDocumentId: docId);

        Console.WriteLine($"→ Triggering: source={source?.ToString() ?? "ALL"}, full={full}, docId={docId ?? "-"}");
        await _trigger.TriggerAsync(options, cancellationToken);
    }

    private async Task HandleLexicalSearchAsync(string line, string[] parts, CancellationToken cancellationToken)
    {
        // Usage: search-sql <source> "query text"
        if (!_sqlChunkStore.Enabled)
        {
            Console.WriteLine("PostgreSQL lexical store is disabled (Indexing:Postgres:Enabled=false).");
            return;
        }

        if (parts.Length < 3)
        {
            Console.WriteLine("Usage: search-sql <source> \"query\"   e.g. search-sql wikijs \"Humanet August update\"");
            return;
        }

        var source = NormalizeSource(parts[1]);

        // Everything after the source token is the query; strip surrounding quotes.
        var sourceTokenEnd = line.IndexOf(parts[1], StringComparison.OrdinalIgnoreCase) + parts[1].Length;
        var query = line[sourceTokenEnd..].Trim().Trim('"', '\'').Trim();
        if (query.Length == 0)
        {
            Console.WriteLine("Empty query.");
            return;
        }

        try
        {
            var results = await _sqlChunkStore.SearchAsync(source, query, limit: 10, cancellationToken);
            Console.WriteLine($"→ {results.Count} result(s) for [{source}] \"{query}\":");
            foreach (var r in results)
            {
                var preview = r.Text.Length > 160 ? r.Text[..160].Replace('\n', ' ') + "…" : r.Text.Replace('\n', ' ');
                Console.WriteLine($"  [{r.Rank:F4}] {r.ChunkId}  «{r.Title}»");
                if (!string.IsNullOrWhiteSpace(r.HeadingPath))
                    Console.WriteLine($"        heading: {r.HeadingPath}");
                Console.WriteLine($"        {preview}");
            }
            if (results.Count == 0)
                Console.WriteLine("  (no matches)");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lexical search failed for source {Source}, query '{Query}'.", source, query);
            Console.WriteLine($"Search failed: {ex.Message}");
        }
    }

    private static string NormalizeSource(string token) => token.ToLowerInvariant() switch
    {
        "wikijs" or "wiki.js" or "wiki" => SourceSystemType.WikiJs.ToString(),
        _ => token
    };

    private static void PrintHelp()
    {
        Console.WriteLine();
        Console.WriteLine("USE.Indexing.Worker — interactive commands:");
        Console.WriteLine("  index                       incremental, all sources");
        Console.WriteLine("  index full                  full reindex, all sources");
        Console.WriteLine("  index wikijs                incremental, only Wiki.js");
        Console.WriteLine("  index wikijs full           full reindex, only Wiki.js");
        Console.WriteLine("  index wikijs <id>           reindex a single Wiki.js page");
        Console.WriteLine("  search-sql wikijs \"query\"   PostgreSQL lexical/BM25 search test");
        Console.WriteLine("  help                        show this help");
        Console.WriteLine("  quit | exit                 stop the worker");
        Console.WriteLine();
    }
}

