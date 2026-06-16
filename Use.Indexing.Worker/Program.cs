using Microsoft.Extensions.Options;
using Use.Indexing.Worker.Chunking;
using Use.Indexing.Worker.Configuration;
using Use.Indexing.Worker.Connectors;
using Use.Indexing.Worker.Connectors.WikiJs;
using Use.Indexing.Worker.Diagnostics;
using Use.Indexing.Worker.Embeddings;
using Use.Indexing.Worker.HostedServices;
using Use.Indexing.Worker.Normalization;
using Use.Indexing.Worker.Orchestration;
using Use.Indexing.Worker.Parsing;
using Use.Indexing.Worker.Parsing.Markdown;
using Use.Indexing.Worker.Persistence;
using Use.Indexing.Worker.Persistence.Postgres;

var builder = Host.CreateApplicationBuilder(args);

// Configuration: bind "Indexing" section. Environment variables can override
// any setting using the standard convention, e.g.:
//   Indexing__Interval=00:05:00
//   Indexing__WikiJs__BaseUrl=https://wiki.internal
builder.Services
    .AddOptions<IndexingOptions>()
    .Bind(builder.Configuration.GetSection(IndexingOptions.SectionName))
    .ValidateOnStart();

// Pipeline stages — all behind interfaces so each can be swapped independently.
builder.Services.AddSingleton<MarkdownDocumentParser>();
builder.Services.AddSingleton<IDocumentParser, DefaultDocumentParser>();
builder.Services.AddSingleton<ITextNormalizer, DefaultTextNormalizer>();
builder.Services.AddSingleton<CharacterWindowChunkingService>(); // fallback for unstructured docs
builder.Services.AddSingleton<IChunkingService, StructureAwareChunkingService>();

// Embeddings: HTTP call to Use.LlmService.Api (which delegates to local Ollama).
builder.Services.AddHttpClient(LlmServiceEmbeddingService.HttpClientName, (sp, http) =>
{
    var opts = sp.GetRequiredService<IOptions<IndexingOptions>>().Value.LlmService;
    http.BaseAddress = new Uri(opts.BaseUrl.TrimEnd('/') + "/");
    http.Timeout = opts.RequestTimeout;
});
builder.Services.AddSingleton<IEmbeddingTextBuilder, EmbeddingTextBuilder>();
builder.Services.AddSingleton<IEmbeddingService, StubEmbeddingService>();
builder.Services.AddSingleton<IIndexRepository, InMemoryIndexRepository>();
builder.Services.AddSingleton<IVectorStore, QdrantVectorStore>();

// PostgreSQL lexical/BM25 chunk store. Independent of Qdrant: same chunk ids,
// but holds clean + searchable text for full-text retrieval. No-ops when
// Indexing:Postgres:Enabled is false.
builder.Services.AddSingleton<ISearchTextBuilder, SearchTextBuilder>();
builder.Services.AddSingleton<ISqlChunkRepository, PostgresIndexRepository>();

// Diagnostics: dump normalized text + chunks to disk for inspection.
builder.Services.AddSingleton<IChunkDumpWriter, ChunkDumpWriter>();

// Source connectors. Add new ones (Azure DevOps Wiki, SharePoint, ...) here;
// the orchestrator picks them all up via IEnumerable<ISourceConnector>.
builder.Services.AddHttpClient<WikiJsGraphQlClient>((sp, http) =>
{
    var opts = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<IndexingOptions>>().Value.WikiJs;
    http.Timeout = opts.RequestTimeout;
});
builder.Services.AddSingleton<ISourceConnector, WikiJsConnector>();

// Orchestration + scheduling + triggers.
builder.Services.AddSingleton<IIndexingScheduleProvider, FixedIntervalScheduleProvider>();
builder.Services.AddSingleton<IReindexTriggerHandler, ChannelReindexTriggerHandler>();
builder.Services.AddSingleton<IIndexingOrchestrator, IndexingOrchestrator>();

// Hosted services: scheduled cycle + event-triggered listener + interactive CLI.
builder.Services.AddHostedService<IndexingWorker>();
builder.Services.AddHostedService<ReindexTriggerListener>();
builder.Services.AddHostedService<ConsoleCommandListener>();

var host = builder.Build();
host.Run();