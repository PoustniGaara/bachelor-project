using Microsoft.Extensions.Options;
using Use.Application.Service.Configuration;
using Use.Application.Service.Evaluation;
using Use.Application.Service.Services.Chat;
using Use.Application.Service.Services.Embeddings;
using Use.Application.Service.Services.Identity;
using Use.Application.Service.Services.LexicalSearch;
using Use.Application.Service.Services.Persistence;
using Use.Application.Service.Services.Prompting;
using Use.Application.Service.Services.Rag;
using Use.Application.Service.Services.Rag.Retrieval;
using Use.Application.Service.Services.VectorSearch;

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------------------
// MVC controllers + Swagger / OpenAPI
// ---------------------------------------------------------------------------
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Exposes HttpContext to services (used by ICurrentUserService to read identity
// headers today; will read the validated JWT claims once auth is implemented).
builder.Services.AddHttpContextAccessor();

// Permissive CORS for the demo frontend. Tighten before any real deployment.
const string FrontendCorsPolicy = "FrontendCors";
builder.Services.AddCors(options =>
{
    options.AddPolicy(FrontendCorsPolicy, p => p
        .AllowAnyOrigin()
        .AllowAnyHeader()
        .AllowAnyMethod());
});

// ---------------------------------------------------------------------------
// Strongly typed configuration
// ---------------------------------------------------------------------------
builder.Services
    .AddOptions<LlmServiceOptions>()
    .Bind(builder.Configuration.GetSection(LlmServiceOptions.SectionName))
    .ValidateDataAnnotations();

builder.Services
    .AddOptions<QdrantOptions>()
    .Bind(builder.Configuration.GetSection(QdrantOptions.SectionName));

builder.Services
    .AddOptions<RagOptions>()
    .Bind(builder.Configuration.GetSection(RagOptions.SectionName));

builder.Services
    .AddOptions<PostgresOptions>()
    .Bind(builder.Configuration.GetSection(PostgresOptions.SectionName));

builder.Services
    .AddOptions<RagEvaluationOptions>()
    .Bind(builder.Configuration.GetSection(RagEvaluationOptions.SectionName));

// ---------------------------------------------------------------------------
// Typed HttpClient for the LLM service (managed by HttpClientFactory).
// ---------------------------------------------------------------------------
builder.Services.AddHttpClient<ILlmServiceClient, LlmServiceClient>((sp, client) =>
{
    var options = sp.GetRequiredService<IOptions<LlmServiceOptions>>().Value;
    client.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/");
    // Take the largest configured timeout; per-call cancellation tokens still apply.
    var maxTimeout = options.GenerationTimeout;
    if (options.EmbeddingTimeout > maxTimeout) maxTimeout = options.EmbeddingTimeout;
    if (options.RerankTimeout > maxTimeout) maxTimeout = options.RerankTimeout;
    client.Timeout = maxTimeout;
});

// ---------------------------------------------------------------------------
// Application services
// ---------------------------------------------------------------------------
builder.Services.AddSingleton<IVectorSearchService, QdrantVectorSearchService>();

// Shared PostgreSQL connection pool (single NpgsqlDataSource) used by BOTH the
// lexical search service and the chat-history / RAG-logging repositories. A
// no-op when Postgres:Enabled=false, so it is safe to register unconditionally.
builder.Services.AddSingleton<IPostgresDataSourceProvider, PostgresDataSourceProvider>();

// Lexical (PostgreSQL full-text) search over the shared pool.
builder.Services.AddSingleton<ILexicalSearchService, PostgresLexicalSearchService>();

// Hybrid retrieval: semantic + lexical fused with Reciprocal Rank Fusion.
builder.Services.AddScoped<IHybridRetrievalService, HybridRetrievalService>();

builder.Services.AddSingleton<
    Use.Application.Service.Services.Rag.DocumentSelection.IDocumentSelector,
    Use.Application.Service.Services.Rag.DocumentSelection.TopDocumentSelector>();
builder.Services.AddSingleton<
    Use.Application.Service.Services.Rag.DocumentSelection.IHybridDocumentSelector,
    Use.Application.Service.Services.Rag.DocumentSelection.HybridDocumentSelector>();
builder.Services.AddScoped<
    Use.Application.Service.Services.Rag.ContextAssembly.IContextAssembler,
    Use.Application.Service.Services.Rag.ContextAssembly.OrderedDocumentContextAssembler>();
builder.Services.AddSingleton<IPromptBuilder, RagPromptBuilder>();
builder.Services.AddScoped<IRagOrchestrator, RagOrchestrator>();

// ---------------------------------------------------------------------------
// Chat history + RAG logging (SQL-backed) — see README §"Chat history".
//
// Identity is NOT real auth yet: ICurrentUserService resolves the caller from
// dev headers (or a Development fallback). Repositories use direct SQL (Npgsql)
// over the shared pool, matching the lexical-search style. All reads are scoped
// to the current user.
// ---------------------------------------------------------------------------
builder.Services.AddScoped<IAppUserRepository, AppUserRepository>();
builder.Services.AddScoped<IChatSessionRepository, ChatSessionRepository>();
builder.Services.AddScoped<IChatMessageRepository, ChatMessageRepository>();
builder.Services.AddScoped<IRagQueryLogRepository, RagQueryLogRepository>();
builder.Services.AddScoped<IRagRetrievedChunkLogRepository, RagRetrievedChunkLogRepository>();
builder.Services.AddScoped<IChunkReferenceResolver, ChunkReferenceResolver>();

builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();
builder.Services.AddScoped<IChatApplicationService, ChatApplicationService>();

// ---------------------------------------------------------------------------
// Retrieval evaluation harness (optional, off by default).
//
// Inert unless Evaluation:EvaluationModeEnabled=true. The hosted service runs a
// retrieval-only evaluation on startup and writes a JSON/CSV report. It reuses
// the exact same retrieval services as chat (RagOrchestrator implements
// IRetrievalProbe) and never calls prompt building or LLM generation. Remove
// this block + the Evaluation/ folder to drop the feature entirely.
// ---------------------------------------------------------------------------
builder.Services.AddSingleton<IRagEvaluationCaseLoader, RagEvaluationCaseLoader>();
builder.Services.AddSingleton<IRetrievalEvaluator, RetrievalEvaluator>();
builder.Services.AddSingleton<IRagEvaluationReportWriter, RagEvaluationReportWriter>();
builder.Services.AddScoped<IRetrievalProbe>(sp => (IRetrievalProbe)sp.GetRequiredService<IRagOrchestrator>());
builder.Services.AddScoped<RagEvaluationRunner>();
builder.Services.AddHostedService<RagEvaluationHostedService>();

// TODO: register JWT (Microsoft Entra ID) authentication + authorization handlers here,
//       then replace the header/dev identity in CurrentUserService with token claims.
// TODO: register source/document permission filtering once authorization lands.

var app = builder.Build();

// ---------------------------------------------------------------------------
// HTTP pipeline
// ---------------------------------------------------------------------------
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseCors(FrontendCorsPolicy);
app.UseAuthorization();
app.MapControllers();

app.Run();
