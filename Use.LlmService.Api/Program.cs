using Microsoft.Extensions.Options;
using Use.LlmService.Api.Configuration;
using Use.LlmService.Api.Providers.Chat;
using Use.LlmService.Api.Providers.Embeddings;
using Use.LlmService.Api.Providers.Reranking;
using Use.LlmService.Api.Services;

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------------------
// MVC controllers + Swagger / OpenAPI
// ---------------------------------------------------------------------------
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// ---------------------------------------------------------------------------
// Strongly typed configuration
// ---------------------------------------------------------------------------
builder.Services
    .AddOptions<LlmOptions>()
    .Bind(builder.Configuration.GetSection(LlmOptions.SectionName))
    .ValidateDataAnnotations();

builder.Services
    .AddOptions<OllamaOptions>()
    .Bind(builder.Configuration.GetSection(OllamaOptions.SectionName))
    .ValidateDataAnnotations();

builder.Services
    .AddOptions<GoogleAiOptions>()
    .Bind(builder.Configuration.GetSection(GoogleAiOptions.SectionName));
    // Validation deferred: the API key is only required when the
    // GoogleAi chat provider is actually selected (see below).

builder.Services
    .AddOptions<RerankerOptions>()
    .Bind(builder.Configuration.GetSection(RerankerOptions.SectionName));

// ---------------------------------------------------------------------------
// Typed HttpClients for the providers
// (HttpClientFactory manages the underlying connection pool).
// ---------------------------------------------------------------------------
builder.Services.AddHttpClient<OllamaEmbeddingProvider>((sp, client) =>
{
    var options = sp.GetRequiredService<IOptions<OllamaOptions>>().Value;
    client.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/");
    client.Timeout = TimeSpan.FromMinutes(2);
});

builder.Services.AddHttpClient<OllamaChatProvider>((sp, client) =>
{
    var options = sp.GetRequiredService<IOptions<OllamaOptions>>().Value;
    client.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/");
    client.Timeout = TimeSpan.FromMinutes(5);
});

// Typed HttpClient for the Google AI (cloud Gemma) chat provider.
builder.Services.AddHttpClient<GoogleAiChatProvider>((sp, client) =>
{
    var options = sp.GetRequiredService<IOptions<GoogleAiOptions>>().Value;
    client.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/");
    client.Timeout = TimeSpan.FromMinutes(5);
});

// Typed HttpClient for the BGE reranker (external Python FastAPI service).
builder.Services.AddHttpClient<BgeRerankingProvider>((sp, client) =>
{
    var options = sp.GetRequiredService<IOptions<RerankerOptions>>().Value;
    client.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/");
    client.Timeout = options.Timeout;
});

// ---------------------------------------------------------------------------
// Provider selection
//
// Only "Ollama" is supported today. Additional providers (OpenAI,
// Azure OpenAI, ...) can be plugged in here without changing the
// controllers or the application services.
// ---------------------------------------------------------------------------
builder.Services.AddScoped<IEmbeddingProvider>(sp =>
{
    var llm = sp.GetRequiredService<IOptions<LlmOptions>>().Value;
    return llm.EmbeddingProvider.ToLowerInvariant() switch
    {
        "ollama" => sp.GetRequiredService<OllamaEmbeddingProvider>(),
        _ => throw new InvalidOperationException(
            $"Unsupported embedding provider '{llm.EmbeddingProvider}'.")
    };
});

builder.Services.AddScoped<IChatProvider>(sp =>
{
    var llm = sp.GetRequiredService<IOptions<LlmOptions>>().Value;
    return llm.ChatProvider.ToLowerInvariant() switch
    {
        "ollama" => sp.GetRequiredService<OllamaChatProvider>(),
        "googleai" or "google" or "gemini" or "cloud"
            => sp.GetRequiredService<GoogleAiChatProvider>(),
        _ => throw new InvalidOperationException(
            $"Unsupported chat provider '{llm.ChatProvider}'.")
    };
});

builder.Services.AddScoped<IRerankingProvider>(sp =>
{
    var reranker = sp.GetRequiredService<IOptions<RerankerOptions>>().Value;
    return reranker.Provider.ToLowerInvariant() switch
    {
        "bge" => sp.GetRequiredService<BgeRerankingProvider>(),
        _ => throw new InvalidOperationException(
            $"Unsupported reranking provider '{reranker.Provider}'.")
    };
});

// ---------------------------------------------------------------------------
// Application services
// ---------------------------------------------------------------------------
builder.Services.AddScoped<IEmbeddingService, EmbeddingService>();
builder.Services.AddScoped<IChatCompletionService, ChatCompletionService>();
builder.Services.AddScoped<IRerankingService, RerankingService>();

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
app.UseAuthorization();
app.MapControllers();

app.Run();
