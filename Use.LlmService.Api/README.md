# Use.LlmService.Api

A centralized **AI gateway service** for the Unified Search Engine solution.
`Use.LlmService.Api` is a stateless ASP.NET Core (.NET 8) Web API that exposes a
small, provider-agnostic HTTP surface for the AI capabilities the platform
needs:

- **Text embeddings** — turn text into vectors for semantic search/indexing.
- **Chat completions** — generate natural-language answers from a prompt.
- **Reranking** — reorder candidate chunks by relevance to a query (cross-encoder).

All other services in the solution (the **Indexing Worker** and the
**Application Service**) depend on this single API instead of talking to LLM
runtimes directly. This keeps model selection, provider credentials, timeouts,
and prompt-to-provider mapping in one place, so the rest of the system can swap
or upgrade models without any code changes.

---

## Why this service exists

In a microservice setup, multiple components need AI capabilities:

| Consumer | What it needs | Endpoint it calls |
| --- | --- | --- |
| `Use.Indexing.Worker` | Embed document chunks before storing them in the vector DB | `POST /api/embeddings` |
| `Use.Application.Service` | Embed user queries + rerank candidate chunks + generate answers (RAG) | `POST /api/embeddings`, `POST /api/rerank`, `POST /api/chat` |

Rather than each service embedding its own Ollama/Google client, they all call
this service. Benefits:

- **Single source of truth** for which models/providers are in use.
- **Provider abstraction** — switch from local Ollama to cloud Google AI by
  changing configuration only.
- **Centralized secrets** — API keys live here, not scattered across consumers.
- **Consistent contracts** — every consumer receives the same provider-agnostic
  request/response shapes.

---

## Architecture

The service is built around a clean layering with two parallel pipelines
(embeddings and chat):

```
HTTP request
   │
   ▼
Controller            (EmbeddingsController / ChatController)
   │   validates input, maps HTTP <-> domain
   ▼
Application Service   (IEmbeddingService / IChatCompletionService)
   │   cross-cutting concerns: validation, logging, telemetry
   ▼
Provider Abstraction  (IEmbeddingProvider / IChatProvider)
   │   selected at startup from configuration
   ▼
Concrete Provider     (Ollama* / GoogleAi*)
   │   typed HttpClient
   ▼
External AI runtime   (local Ollama  /  Google Generative Language API)
```

The provider is chosen **once at startup** in `Program.cs` based on the `Llm`
configuration section, so controllers and application services never know which
backend is actually being used.

### Folder layout

| Folder | Responsibility |
| --- | --- |
| `Controllers/` | HTTP endpoints (`ChatController`, `EmbeddingsController`, `RerankController`). Input validation + error-to-status mapping. |
| `Services/` | Application services (`IEmbeddingService`, `IChatCompletionService`, `IRerankingService`) that sit between controllers and providers for cross-cutting concerns. |
| `Providers/Embeddings/` | `IEmbeddingProvider` abstraction + `OllamaEmbeddingProvider`. |
| `Providers/Chat/` | `IChatProvider` abstraction + `OllamaChatProvider`, `GoogleAiChatProvider`. |
| `Providers/Reranking/` | `IRerankingProvider` abstraction + `BgeRerankingProvider` (calls the Python reranker service). |
| `Models/` | Provider-agnostic request/response DTOs + provider-native payloads (Ollama, Google). |
| `Configuration/` | Strongly typed options bound from `appsettings.json` (`LlmOptions`, `OllamaOptions`, `GoogleAiOptions`, `RerankerOptions`). |
| `Program.cs` | DI wiring, typed HttpClients, provider selection, Swagger, HTTP pipeline. |

---

## Supported providers

| Capability | Provider | Backend | Default model |
| --- | --- | --- | --- |
| Embeddings | `Ollama` | Local Ollama HTTP API (`/api/embed`) | `embeddinggemma` |
| Chat | `Ollama` | Local Ollama HTTP API (`/api/generate`) | `gemma3:12b` |
| Chat | `GoogleAi` (aliases: `google`, `gemini`, `cloud`) | Google Generative Language API (`models/{model}:generateContent`) | `gemma-4-31b-it` |
| Reranking | `Bge` | `Use.Reranker.Service` (Python FastAPI, `/rerank`) | `BAAI/bge-reranker-v2-m3` |

Provider selection is driven by the `Llm:EmbeddingProvider`,
`Llm:ChatProvider` and `Reranker:Provider` settings. Unsupported values cause a
startup/resolution failure with a clear message, and the Google API key is only
validated when the Google chat provider is actually selected.

> The abstraction (`IEmbeddingProvider` / `IChatProvider` /
> `IRerankingProvider`) makes it straightforward to add new backends (OpenAI,
> Azure OpenAI, Cohere rerank, ...) without touching the controllers or
> application services.

---

## API Reference

Base URL (development): `http://localhost:5133` (HTTP) / `https://localhost:7178` (HTTPS).
Interactive docs (Swagger UI) are available at `/swagger` in the Development
environment.

### `POST /api/embeddings`

Generate an embedding vector for a piece of text. Used primarily by the
Indexing Worker.

**Request body** (`EmbeddingRequest`):

```json
{
  "input": "The text to embed",
  "sourceType": "DocumentChunk"
}
```

| Field | Type | Required | Description |
| --- | --- | --- | --- |
| `input` | string | ✅ | Text to convert into an embedding. |
| `sourceType` | string | ❌ | Free-form hint (e.g. `DocumentChunk`, `UserQuery`) used for telemetry/routing only — does not affect the model. |

**Response** (`EmbeddingResponse`, `200 OK`):

```json
{
  "model": "embeddinggemma",
  "dimensions": 768,
  "embedding": [0.0123, -0.0456, "..."]
}
```

| Status | Meaning |
| --- | --- |
| `200 OK` | Embedding produced. |
| `400 Bad Request` | `input` was empty/whitespace. |
| `502 Bad Gateway` | The embedding provider/runtime was unavailable or returned an invalid response. |

### `POST /api/chat`

Generate a natural-language answer for a prompt. Used primarily by the
Application Service (e.g. for RAG answers).

**Request body** (`ChatRequest`):

```json
{
  "prompt": "What is the capital of France?",
  "systemPrompt": "You are a concise assistant. Answer in one sentence."
}
```

| Field | Type | Required | Description |
| --- | --- | --- | --- |
| `prompt` | string | ✅ | The user question / prompt. |
| `systemPrompt` | string | ❌ | Optional system instruction that steers the model. |

**Response** (`ChatResponse`, `200 OK`):

```json
{
  "model": "gemma-4-31b-it",
  "answer": "The capital of France is Paris."
}
```

| Status | Meaning |
| --- | --- |
| `200 OK` | Answer generated. |
| `400 Bad Request` | `prompt` was empty/whitespace. |
| `502 Bad Gateway` | The chat provider/runtime was unavailable or returned an invalid response. |

> For the Google AI provider, the `systemPrompt` is mapped to the native
> `systemInstruction` field, and any internal "thinking" parts of the model's
> response are filtered out before the answer is returned.

### `POST /api/rerank`

Reorder candidate chunks by their relevance to a query. Used by the Application
Service after hybrid retrieval + RRF fusion, before document selection. The
request is forwarded to the configured reranker provider (`Bge` →
`Use.Reranker.Service`), keeping the concrete model hidden from callers.

**Request body** (`RerankRequest`):

```json
{
  "query": "user question",
  "documents": [
    { "chunkId": "WikiJs:265:7", "text": "candidate chunk text" }
  ]
}
```

| Field | Type | Required | Description |
| --- | --- | --- | --- |
| `query` | string | ✅ | The user question the documents are scored against. |
| `documents` | array | ✅ | Candidate chunks to score. Each needs a non-empty `chunkId` and `text`. |

**Response** (`RerankResponse`, `200 OK`, sorted by `score` descending):

```json
{
  "model": "BAAI/bge-reranker-v2-m3",
  "results": [
    { "chunkId": "WikiJs:265:7", "score": 0.934 }
  ]
}
```

| Status | Meaning |
| --- | --- |
| `200 OK` | Scores produced. |
| `400 Bad Request` | `query` empty, `documents` empty, or a `chunkId`/`text` empty. |
| `502 Bad Gateway` | The reranker provider/service was unavailable or returned an invalid response. |

---

## Configuration

All settings live in `appsettings.json` (overridable per environment and via
environment variables / user secrets).

```json
{
  "Llm": {
    "EmbeddingProvider": "Ollama",
    "ChatProvider": "GoogleAi"
  },
  "Ollama": {
    "BaseUrl": "http://localhost:11434",
    "EmbeddingModel": "embeddinggemma",
    "ChatModel": "gemma3:12b"
  },
  "GoogleAi": {
    "BaseUrl": "https://generativelanguage.googleapis.com/v1beta/",
    "ChatModel": "gemma-4-31b-it",
    "ApiKey": "<set-via-secrets>",
    "Temperature": 0.2,
    "MaxOutputTokens": 2048,
    "ThinkingLevel": "high"
  },
  "Reranker": {
    "Provider": "Bge",
    "BaseUrl": "http://localhost:8000",
    "Endpoint": "/rerank",
    "Timeout": "00:02:00",
    "Model": "BAAI/bge-reranker-v2-m3"
  }
}
```

### `Llm` (provider selection)

| Key | Description | Default |
| --- | --- | --- |
| `EmbeddingProvider` | Which provider produces embeddings. Supported: `Ollama`. | `Ollama` |
| `ChatProvider` | Which provider produces chat answers. Supported: `Ollama`, `GoogleAi` (`google`/`gemini`/`cloud`). | `Ollama` |

### `Ollama` (local runtime)

| Key | Description | Default |
| --- | --- | --- |
| `BaseUrl` | Base URL of the Ollama HTTP API. | `http://localhost:11434` |
| `EmbeddingModel` | Model used for embeddings. | `embeddinggemma` |
| `ChatModel` | Model used for chat. | `gemma3:4b` |

### `GoogleAi` (cloud runtime)

| Key | Description | Default |
| --- | --- | --- |
| `BaseUrl` | Base URL of the Generative Language API. | `https://generativelanguage.googleapis.com/v1beta/` |
| `ChatModel` | Cloud Gemma model id. | `gemma-4-31b-it` |
| `ApiKey` | Google AI Studio API key. **Required** when the Google provider is selected. | _(empty)_ |
| `Temperature` | Sampling temperature (0.0–2.0). | `0.2` |
| `MaxOutputTokens` | Maximum output tokens. | `2048` |
| `ThinkingLevel` | Reasoning budget: `off`, `low`, `medium`, `high`. | `high` |

> **Security note:** Do not commit real API keys to source control. Provide
> `GoogleAi:ApiKey` via [user secrets](https://learn.microsoft.com/aspnet/core/security/app-secrets)
> or environment variables, e.g. `GoogleAi__ApiKey=...`.

### `Reranker` (reranking backend)

| Key | Description | Default |
| --- | --- | --- |
| `Provider` | Which reranker provider is used. Supported: `Bge`. | `Bge` |
| `BaseUrl` | Base URL of the reranker backend (`Use.Reranker.Service`). | `http://localhost:8000` |
| `Endpoint` | Relative path of the rerank endpoint on the backend. | `/rerank` |
| `Timeout` | HTTP timeout for rerank calls. | `00:02:00` |
| `Model` | Logical model id reported in responses/logs. | `BAAI/bge-reranker-v2-m3` |

> All keys are overridable via environment variables, e.g.
> `Reranker__BaseUrl=http://reranker-service:8000`, `Reranker__Endpoint=/rerank`,
> `Reranker__Provider=Bge`, `Reranker__Timeout=00:02:00`. Inside Docker Compose
> the gateway reaches the reranker at `http://reranker-service:8000/rerank`.

---

## Running the service

### Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download)
- For local models: a running [Ollama](https://ollama.com) instance with the
  configured models pulled:
  ```bash
  ollama pull embeddinggemma
  ollama pull gemma3:12b
  ```
- For cloud chat: a Google AI Studio API key.

### Start

```bash
cd Use.LlmService.Api
dotnet run
```

By default the service listens on `http://localhost:5133` and opens Swagger UI
at `/swagger` in Development.

### Quick test

```bash
# Embedding
curl -X POST http://localhost:5133/api/embeddings \
  -H "Content-Type: application/json" \
  -d '{"input":"hello world","sourceType":"UserQuery"}'

# Chat
curl -X POST http://localhost:5133/api/chat \
  -H "Content-Type: application/json" \
  -d '{"prompt":"Say hi in one word."}'

# Rerank (requires Use.Reranker.Service running on :8000)
curl -X POST http://localhost:5133/api/rerank \
  -H "Content-Type: application/json" \
  -d '{
    "query": "Kedy je naplánovaný update systému Humanet pre mesiac August?",
    "documents": [
      { "chunkId": "1", "text": "Mesiac: August; Dátum pre update: 22. 8. 2025." },
      { "chunkId": "2", "text": "Táto stránka popisuje používateľské oprávnenia." }
    ]
  }'
```

---

## Operational notes

- **Stateless** — the service holds no database or session state; it can be
  scaled horizontally behind a load balancer.
- **Typed HttpClients** — connection pooling is managed by
  `IHttpClientFactory`. Embedding calls use a 2-minute timeout; chat calls use a
  5-minute timeout to accommodate slower generation.
- **Resilient error mapping** — provider/network failures surface as
  `502 Bad Gateway` with a descriptive message; invalid input surfaces as
  `400 Bad Request`. Errors are logged with provider context.
- **Extensible** — add a new provider by implementing `IEmbeddingProvider`,
  `IChatProvider` or `IRerankingProvider`, registering a typed HttpClient, and
  adding a case to the selection switch in `Program.cs`.

---

## Tech stack

- ASP.NET Core Web API (.NET 8, nullable + implicit usings enabled)
- `Microsoft.AspNetCore.OpenApi` + `Swashbuckle.AspNetCore` (Swagger/OpenAPI)
- `Microsoft.Extensions.Options` with data-annotation validation
- `System.Net.Http.Json` for provider calls

