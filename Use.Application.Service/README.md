# Use.Application.Service — RAG Query Pipeline

The application service is a .NET 8 ASP.NET Core Web API
(`Microsoft.NET.Sdk.Web`) that answers documentation questions from a web
frontend. It does **not** index anything itself — it queries the artifacts
produced by `Use.Indexing.Worker`:

- the **Qdrant** collection (vector / semantic search + full-document expansion), and
- the **PostgreSQL** chunk store (lexical / BM25-ready full-text search).

It orchestrates calls to the **`Use.LlmService.Api`** (which proxies a local
**Ollama** model) for both embedding and generation.

Retrieval is now **hybrid**: a semantic (vector) pass and a lexical (full-text)
pass are fused with **Reciprocal Rank Fusion (RRF)**, and the top fused
candidates are then **reranked** by a dedicated cross-encoder
(`BAAI/bge-reranker-v2-m3`, via `Use.LlmService.Api /api/rerank`) before document
selection. The mode is configurable (`Rag:RetrievalMode` — `SemanticOnly`,
`LexicalOnly`, `Hybrid`), reranking is toggleable (`Rag:RerankingEnabled`), and
the *legacy semantic-only pipeline is preserved verbatim* under `SemanticOnly`.

This document describes the **current state** of the project end-to-end:
configuration, every retrieval stage, the document-selection policy, the
prompt contract, and the HTTP surface. Keep it in sync with the code when
behavior changes. It is also the baseline used by the evaluation report —
when retrieval behavior changes, bump this file and re-run the evaluation.

> **New: SQL-backed chat history + RAG logging.** The service now persists
> users, chat sessions, chat messages, and per-request RAG telemetry to the same
> PostgreSQL database used for lexical search (tables `app_user`, `chat_session`,
> `chat_message`, `rag_query_log`, `rag_retrieved_chunk_log`). **Authentication
> is not implemented yet** — the current user is resolved from dev headers (or a
> Development fallback) behind `ICurrentUserService`, ready to be swapped for
> Microsoft Entra ID / JWT later. See **§11b** for the full design.

---

## 1. High-level pipeline

```
                      ┌───────────────────────────────┐
   HTTP POST          │  ChatController.Ask            │
   /api/chat ───────▶│  (Controllers/                 │
                      │   ChatController.cs)           │
                      └────────────┬──────────────────┘
                                   │
                                   ▼
                      ┌───────────────────────────────┐
                      │  IRagOrchestrator              │
                      │  (Services/Rag/                │
                      │   RagOrchestrator.cs)          │
                      │  resolves Rag:RetrievalMode    │
                      └────────────┬──────────────────┘
                                   │
                                   ▼
   1. Embed question      ── ILlmServiceClient.CreateEmbeddingAsync
      (skipped in LexicalOnly)     │
                                   ▼
   2. Hybrid retrieval     ── IHybridRetrievalService.RetrieveAsync
      ├─ semantic pass      ──   IVectorSearchService.SearchAsync (Rag:InitialTopK)
      ├─ lexical pass       ──   ILexicalSearchService.SearchAsync (Rag:LexicalTopK)
      └─ RRF fusion         ──   merge by chunkId → FusedChunkResult[]
                                   │
                                   ▼
   2b. Reranking           ── ILlmServiceClient.RerankAsync (Rag:RerankingEnabled)
       (top Rag:RerankTopK)        take top 30 fused chunks → /api/rerank →
                                    BAAI/bge-reranker-v2-m3 → reorder by score,
                                    map RerankScore back onto FusedChunkResult[]
                                   │
                                   ▼
   3. Document selection  ── IHybridDocumentSelector.Select
      (top-3 documents)            (group fused chunks by sourceDocumentId,
                                    rank by rerank score when available,
                                    else fused document score)
                                   │
                                   ▼
   4. Context assembly    ── IContextAssembler.AssembleAsync
      (full ordered chunks)        (Qdrant scroll per docId,
                                    order by chunkOrder ascending)
                                   │
                                   ▼
   5. Prompt building     ── IPromptBuilder.BuildUserPrompt
      (grouped per document)       (system rules + grouped, ordered context)
                                   │
                                   ▼
   6. Generation          ── ILlmServiceClient.GenerateAsync
                                   │
                                   ▼
   7. Response assembly   ── ChatResponse { Answer, Sources, RetrievedChunks }
```

> **`SemanticOnly` mode** keeps the original flow: embed → vector search
> (`IVectorSearchService.SearchAsync`) → `IDocumentSelector` (hit-count ranking)
> → context assembly → generation. No PostgreSQL dependency is required.

Each stage hides behind an interface, so any single piece can be swapped
without touching the orchestrator. See `Program.cs` for the full DI wiring.

---

## 2. HTTP surface

| File | Role |
|---|---|
| `Controllers/ChatController.cs` | The HTTP surface (chat + history + rating endpoints). Validates input, delegates to `IChatApplicationService`, maps failures to status codes. |
| `Program.cs` | Builds the DI container, binds options, registers the typed `HttpClient` for the LLM API, the shared Postgres pool + repositories, enables Swagger + CORS. |

### 2.1 Endpoints

| Method + route | Purpose |
|---|---|
| `POST /api/chat` | Run the RAG pipeline for a question; create/continue a chat session and persist the conversation + RAG logs. |
| `GET /api/chat/sessions` | List the current user's chat sessions (newest first). |
| `POST /api/chat/sessions` | Create a new empty chat session (optional — `POST /api/chat` auto-creates one). |
| `GET /api/chat/sessions/{chatSessionId}/messages` | List messages of one session **owned by the current user**. |
| `POST /api/chat/rag-query-logs/{ragQueryLogId}/rating` | Store a rating/feedback against a RAG answer (preferred — the chat response returns `ragQueryLogId`). |
| `POST /api/chat/messages/{messageId}/rating` | Store a rating/feedback resolved from a chat message id (user or assistant). |

#### `POST /api/chat`

**Request body** (`Models/Requests/ChatRequest`):
```json
{
  "question": "Aký parameter určuje URL Humanet REST API?",
  "chatSessionId": null
}
```

- `chatSessionId` **null** → a new session is created and its id returned.
- `chatSessionId` **set** → the session must belong to the current user (else `404`).

**Response body** (`Models/Responses/ChatResponse`):
```json
{
  "answer": "…model-generated text…",
  "sources": [
    {
      "title": "Príkazy",
      "url": "https://wiki.example.local/humanet/integrator/prikazy",
      "sourceSystem": "WikiJs",
      "chunkId": "WikiJs:265:0",
      "score": 0.8123
    }
  ],
  "retrievedChunks": [ /* RetrievedChunk[]: every chunk fed to the LLM */ ],

  "chatSessionId": "…",
  "userMessageId": "…",
  "assistantMessageId": "…",
  "ragQueryLogId": "…"
}
```

The four chat-metadata fields are **additive and nullable** — the existing
frontend contract (`answer`, `sources`, `retrievedChunks`) is unchanged. They are
populated when persistence is enabled, and `null` when `Postgres:Enabled=false`
(the request still answers, without persistence).

**Status codes**

| Code | When |
|---|---|
| `200 OK` | Normal answer (including the "no documentation found" fallback text). |
| `400 Bad Request` | Empty `question`, invalid rating, or an `ArgumentException` from the pipeline. |
| `401 Unauthorized` | No identity could be resolved (outside Development — see §11b). |
| `404 Not Found` | `chatSessionId` / session / log does not belong to the current user. |
| `502 Bad Gateway` | LLM API, Qdrant, or PostgreSQL unreachable / returned an error. |
| `503 Service Unavailable` | A history/rating endpoint was called while `Postgres:Enabled=false`. |

CORS is currently fully permissive (`AllowAnyOrigin/Header/Method`) for the
demo frontend — tighten before any real deployment.

---

## 3. Retrieval policy (the important part)

The retrieval policy is the heart of this service. It deliberately differs
from a "top-K chunks straight into the prompt" baseline because we want the
LLM to reason over **coherent documents**, not a shotgun of fragments.

> **Mental model.** "Find the documents that are most clearly on-topic — using
> *both* meaning (vectors) and *words* (lexical) — then show the model those
> documents *in full and in order*."

### 3.0 Retrieval modes (`Rag:RetrievalMode`)

| Mode | What runs | Postgres required? |
|---|---|---|
| `SemanticOnly` | Qdrant vector search only (legacy pipeline, unchanged). | No |
| `LexicalOnly` | PostgreSQL full-text search only. Debugging / evaluation. | Yes |
| `Hybrid` *(default)* | Vector **and** lexical, fused with RRF. | Yes |

`Hybrid` / `LexicalOnly` need PostgreSQL. If `Postgres:Enabled=false`, the
orchestrator **degrades to `SemanticOnly` in Development** (with a warning) and
**fails clearly outside Development** (`InvalidOperationException` →
`502 Bad Gateway`). If Postgres is enabled but unreachable, the lexical query
throws and bubbles up to the controller as `502`, same as a Qdrant/LLM failure.

### 3.1 Semantic pass (vector, Stage 2a)

- Embed the user question via `ILlmServiceClient.CreateEmbeddingAsync` with
  `SourceType = "UserQuery"`. (Skipped entirely in `LexicalOnly`.)
- One vector search against the configured Qdrant collection:
  `IVectorSearchService.SearchAsync(embedding, Rag:InitialTopK, …)`.
- Pulls **`Rag:InitialTopK`** chunks (default **40**) with payload included.
- No payload filters are applied today (TODO: permission / source filters).

### 3.2 Lexical pass (PostgreSQL full-text, Stage 2b)

Implemented by `Services/LexicalSearch/PostgresLexicalSearchService.cs`.

- One full-text query against `rag_document_chunks` (joined to
  `rag_source_documents` for title/url), ranked by `ts_rank_cd` over the
  `search_vector` GIN-indexed `tsvector` column.
- Uses `websearch_to_tsquery('simple', …)` (robust to arbitrary user input);
  falls back to `plainto_tsquery('simple', …)` if that ever errors.
- Pulls **`Rag:LexicalTopK`** chunks (default **40**).
- Returns the same normalized fields as the vector pass (`chunkId`,
  `sourceSystem`, `sourceDocumentId`, `chunkOrder`, `title`, `url`,
  `headingPath`, `text`, `score`).

> **Why lexical at all?** Vectors are great for paraphrase / meaning but can
> miss exact tokens — product names, dates, command names. A question like
> *"Kedy je naplánovaný update systému Humanet pre mesiac August?"* matches the
> chunk *"Mesiac: August; Dátum pre update: 22. 8. 2025"* on the literal tokens
> `Humanet`, `update`, `August`, which lexical search nails even when the vector
> score is mediocre. PostgreSQL full-text is **not true BM25**, but the chunk
> store is BM25-ready (`search_text` / `search_vector`) and the ranking can be
> upgraded later without changing this contract.

### 3.3 Reciprocal Rank Fusion (Stage 2c)

Implemented by `Services/Rag/Retrieval/HybridRetrievalService.cs`.

The two passes return **incomparable** scores (Qdrant cosine vs. PostgreSQL
`ts_rank_cd`), so we fuse by **rank**, not by raw score:

```
rrfScore(chunk) = Σ  1 / (Rag:RrfK + rank_in_method)
                 methods
```

- `rank_in_method` is the 1-based position of the chunk within that method's
  result list; `Rag:RrfK` defaults to **60**.
- A chunk found by **both** methods accumulates both terms; a chunk found by
  **one** method still gets that method's term.
- Results are merged by `chunkId` into a `FusedChunkResult` carrying
  `FusedScore`, `SemanticRank/Score`, `LexicalRank/Score`.
- Stable ordering: `FusedScore DESC`, then best original rank ASC, then
  `ChunkId ASC`.

*Example:* a chunk at lexical rank 2 and semantic rank 15 scores
`1/(60+2) + 1/(60+15) ≈ 0.0294`.

### 3.3b Reranking (Stage 2b — after fusion, before selection)

Implemented in `Services/Rag/RagOrchestrator.cs` (`RerankFusedAsync`), gated by
`Rag:RerankingEnabled` (default **true**).

RRF fuses by rank but is still blind to *meaning at the pair level*. A
cross-encoder reranker reads the `(query, chunk)` pair jointly and produces a
much sharper relevance signal. We insert it **after** fusion and **before**
document selection:

1. Take the top **`Rag:RerankTopK`** fused chunks (default **30**) — fused
   chunks are already ordered by `FusedScore DESC`. Only these candidates are
   sent; full parent documents are **never** sent to the reranker.
2. Call `ILlmServiceClient.RerankAsync(query, documents)` →
   `Use.LlmService.Api /api/rerank` → `Use.Reranker.Service /rerank` →
   `BAAI/bge-reranker-v2-m3`.
3. Map each returned score back onto its `FusedChunkResult` by `chunkId`
   (`RerankScore`, `WasReranked`, `RerankedRank`).
4. Reorder the candidate block by `RerankScore DESC` (fused score as tie-break);
   fused candidates beyond the top-K keep their fused order and follow the
   reranked block.
5. The reordered list flows into document selection.

The existing fusion telemetry is preserved (`FusedScore`, `SemanticRank/Score`,
`LexicalRank/Score`) — reranking only **adds** `RerankScore` / `WasReranked` /
`RerankedRank`.

- **`Rag:RerankingEnabled = false`** → this stage is a no-op; the fused list
  passes through unchanged and the legacy fused-score selection applies.
- **Reranker failure** → surfaces as a `502 Bad Gateway`, consistent with the
  other LLM-service calls (no silent degradation).

> Reranking runs only on the `Hybrid` / `LexicalOnly` (fusion) paths. The
> legacy `SemanticOnly` path is untouched.

### 3.4 Document selection (Stage 3)

`SemanticOnly` uses `Services/Rag/DocumentSelection/TopDocumentSelector.cs`
(group by document, rank by **hit count**, tie-break by best score).

`Hybrid` / `LexicalOnly` use
`Services/Rag/DocumentSelection/HybridDocumentSelector.cs`, which now prefers the
**reranker** signal when it is available, falling back to fused scores otherwise:

- Group fused chunks by `(sourceSystem, sourceDocumentId)`.
- For each document, **if any chunk has a `RerankScore`** (reranking ran):
  - `BestRerankScore    = max(RerankScore)`
  - `TopRerankScoreSum  = Σ top-3 RerankScore`
  - `HitCount           = chunk count`
  - `DocumentScore = BestRerankScore + TopRerankScoreSum + HitCount * 0.01`
  - `RepresentativeChunkId` = the top **reranked** chunk of that document.
- **Otherwise** (reranking disabled / no scores) the legacy fused formula:
  - `BestScore   = max(FusedScore)`
  - `TopScoreSum = Σ top-3 FusedScore`
  - `DocumentScore = BestScore + TopScoreSum + HitCount * 0.01`
- Order by `DocumentScore DESC`, tie-break `BestScore DESC`, then `HitCount DESC`.
- Take the top **`Rag:TopDocuments`** documents (default **3**).
- Each selection carries telemetry: `HitCount`, `BestScore`, `DocumentScore`,
  and a `RepresentativeChunkId`.
- Chunks with no `sourceDocumentId` are ignored here (they can still surface
  via the fallback path, §3.6).

### 3.5 Document expansion / "context build-up" (Stage 4)

Implemented by
`Services/Rag/ContextAssembly/OrderedDocumentContextAssembler.cs`. **Unchanged**
by hybrid retrieval — full documents are still reconstructed from **Qdrant**.

For each of the selected documents (run in parallel):

1. Issue a **Qdrant scroll** with a payload filter
   `sourceSystem == X AND sourceDocumentId == Y`
   (`IVectorSearchService.ListByDocumentAsync`).
2. Page through results (page size 256) until exhausted or
   `Rag:MaxChunksPerDocument` (default **500**) is reached — a safety cap
   for pathological documents.
3. Order the resulting chunks by `chunkOrder` ascending, with `ChunkId` as a
   secondary deterministic tie-breaker.

The end result is a `DocumentContext` per selected document: an ordered,
gap-free reconstruction of the original document content. The `DocumentScore`
and `RepresentativeChunkId` from selection are carried through for the response
`Sources`.

### 3.6 Prompt construction (Stage 5)

Implemented by `Services/Prompting/RagPromptBuilder.cs`.

- Flatten the selected documents (in selector priority order) into one
  ordered chunk list.
- The prompt builder groups the chunks **by document** under
  `=== Dokument N: <title> ===` headers, including the URL if available.
- Each chunk is rendered in `chunkOrder` and truncated to
  `Rag:MaxChunkCharacters` (default **3000**) defensively.
- A strict Slovak-language system prompt enforces the grounding rules
  (no hallucinations, decline gracefully when context is insufficient, cite
  sources at the end). See §7.

### 3.7 Fallback paths

| Situation | Behavior |
|---|---|
| Retrieval returns 0 chunks (semantic + lexical empty) | Return `"I could not find any relevant documentation for that question."` with empty `sources` and `retrievedChunks`. |
| Hits exist but none has a usable `sourceDocumentId` | Log a warning and degrade to the **legacy** "use the raw chunks straight as context" flow. Lexical rows always carry a doc id, so this is rare; kept for resilience. |
| Selected documents resolve to zero chunks on scroll | Same empty-answer response (the document was probably deleted between search and scroll). |
| `Hybrid`/`LexicalOnly` but `Postgres:Enabled=false` | Degrade to `SemanticOnly` in Development (warning); fail with `502` otherwise. |
| Qdrant / PostgreSQL / LLM API throws | Bubbles up to `ChatController` → `502 Bad Gateway`. |

---

## 4. Component map

All RAG-related code lives under `Use.Application.Service/`. Each row below
corresponds to a single C# interface + implementation.

| Stage | Interface | Default implementation | Responsibility |
|---|---|---|---|
| HTTP | — | `Controllers/ChatController.cs` | Pure HTTP adapter for chat + history + rating endpoints. |
| Chat orchestration | `IChatApplicationService` | `Services/Chat/ChatApplicationService.cs` | Resolves the user, creates/continues sessions, persists messages, writes RAG logs, calls the RAG orchestrator, assembles the response. |
| Current user / identity | `ICurrentUserService` | `Services/Identity/CurrentUserService.cs` | Resolves the current `AppUser` from dev headers (or a Development fallback). **Auth seam** for future Entra ID / JWT. |
| RAG pipeline | `IRagOrchestrator` | `Services/Rag/RagOrchestrator.cs` | Resolves `RetrievalMode`, wires the pipeline, returns the answer + retrieval telemetry (`RagExecutionResult`). No persistence of its own. |
| Embedding + Generation | `ILlmServiceClient` | `Services/Embeddings/LlmServiceClient.cs` | Typed `HttpClient` to `Use.LlmService.Api`. |
| Vector search | `IVectorSearchService` | `Services/VectorSearch/QdrantVectorSearchService.cs` | Both `SearchAsync` (similarity) and `ListByDocumentAsync` (scroll by filter). |
| Lexical search | `ILexicalSearchService` | `Services/LexicalSearch/PostgresLexicalSearchService.cs` | PostgreSQL full-text search over `rag_document_chunks` (Npgsql, direct SQL, shared pool). |
| Postgres pool | `IPostgresDataSourceProvider` | `Services/Persistence/PostgresDataSourceProvider.cs` | Owns the single shared `NpgsqlDataSource` used by lexical search **and** the chat/logging repositories. |
| Users | `IAppUserRepository` | `Services/Persistence/AppUserRepository.cs` | `app_user` get/create/upsert + `last_login_at`. |
| Chat sessions | `IChatSessionRepository` | `Services/Persistence/ChatSessionRepository.cs` | `chat_session` create/list/get/touch (user-scoped). |
| Chat messages | `IChatMessageRepository` | `Services/Persistence/ChatMessageRepository.cs` | `chat_message` create + list (session ownership enforced). |
| RAG query log | `IRagQueryLogRepository` | `Services/Persistence/RagQueryLogRepository.cs` | `rag_query_log` create/complete/fail/cancel/rate + message→log resolution. |
| Retrieved chunk log | `IRagRetrievedChunkLogRepository` | `Services/Persistence/RagRetrievedChunkLogRepository.cs` | Batch insert of `rag_retrieved_chunk_log` rows. |
| Chunk id resolver | `IChunkReferenceResolver` | `Services/Persistence/ChunkReferenceResolver.cs` | Maps stable text chunk ids → `rag_document_chunks.id` + `rag_source_documents.id`. |
| Hybrid retrieval + RRF | `IHybridRetrievalService` | `Services/Rag/Retrieval/HybridRetrievalService.cs` | Runs semantic + lexical passes and fuses by Reciprocal Rank Fusion. |
| Reranking | `ILlmServiceClient.RerankAsync` | `Services/Embeddings/LlmServiceClient.cs` (orchestrated in `RagOrchestrator.RerankFusedAsync`) | Reorders top `Rag:RerankTopK` fused chunks via `Use.LlmService.Api /api/rerank` (BAAI/bge-reranker-v2-m3). |
| Document selection (semantic) | `IDocumentSelector` | `Services/Rag/DocumentSelection/TopDocumentSelector.cs` | Ranks documents by hit count + score (legacy path). |
| Document selection (fused) | `IHybridDocumentSelector` | `Services/Rag/DocumentSelection/HybridDocumentSelector.cs` | Ranks documents by rerank score when available, else fused document score. |
| Context assembly | `IContextAssembler` | `Services/Rag/ContextAssembly/OrderedDocumentContextAssembler.cs` | Per-document Qdrant scroll + order by `chunkOrder`. |
| Prompt building | `IPromptBuilder` | `Services/Prompting/RagPromptBuilder.cs` | System prompt + grouped, ordered user prompt. |

### Models

| File | Purpose |
|---|---|
| `Models/Requests/ChatRequest.cs` | Public HTTP request shape (`Question` + optional `ChatSessionId`). |
| `Models/Requests/CreateChatSessionRequest.cs` | Body of `POST /api/chat/sessions` (optional `Title`). |
| `Models/Requests/RatingRequest.cs` | Body of the rating endpoints (`Rating` = -1/1/null, optional `Feedback`). |
| `Models/Responses/ChatResponse.cs` | Public HTTP response (`Answer`, `Sources`, `RetrievedChunks` + chat metadata `ChatSessionId`/`UserMessageId`/`AssistantMessageId`/`RagQueryLogId`). |
| `Models/Responses/SourceReference.cs` | One citation entry (one per selected document). |
| `Models/Responses/ChatSessionResponse.cs` | One chat session in the history list. |
| `Models/Responses/ChatMessageResponse.cs` | One message in a session. |
| `Models/Chat/AppUser.cs`, `ChatSession.cs`, `ChatMessage.cs` | Domain records mirroring `app_user` / `chat_session` / `chat_message` (+ `ChatMessageRole` constants). |
| `Models/Logging/RagQueryLog.cs`, `RagRetrievedChunkLog.cs`, `RagAnswerStatus.cs` | Domain records + insert/update DTOs mirroring `rag_query_log` / `rag_retrieved_chunk_log`, and the `answer_status` constants. |
| `Models/Retrieval/RetrievedChunk.cs` | Normalized chunk shape (text + payload fields, including `ChunkOrder` and `SourceDocumentId`). |
| `Models/Retrieval/VectorSearchResult.cs` | Aggregated result of one similarity call (chunks + collection + requested top-K). |
| `Models/Retrieval/LexicalSearchResult.cs` | One chunk from the PostgreSQL lexical pass (normalized fields + lexical score). |
| `Models/Retrieval/FusedChunkResult.cs` | One chunk after RRF (fused score + per-method rank/score telemetry + rerank score/rank). |
| `Models/Retrieval/DocumentContext.cs` | A single document plus its ordered chunks + hit-count / best-score / document-score telemetry. |
| `Models/Retrieval/RetrievalCandidate.cs` | Top retrieved candidate (stable chunk id + scores + rank + selected-for-context flag) surfaced for logging. |
| `Models/Retrieval/RagExecutionResult.cs` | The `ChatResponse` plus retrieval telemetry (mode, strategy, timings, candidates, status) returned by `IRagOrchestrator.ExecuteAsync`. |
| `Common/EmbeddingRequest.cs`, `EmbeddingResponse.cs`, `GenerationRequest.cs`, `GenerationResponse.cs`, `RerankRequest.cs`, `RerankDocument.cs`, `RerankResponse.cs`, `RerankResult.cs` | DTOs for the LLM API (embeddings, chat, rerank). |

### Configuration

| File | Section |
|---|---|
| `Configuration/RagOptions.cs` | `"Rag"` |
| `Configuration/RetrievalMode.cs` | enum used by `Rag:RetrievalMode` |
| `Configuration/RetrievalStrategyNames.cs` | maps `RetrievalMode` → `rag_query_log.retrieval_strategy` value |
| `Configuration/QdrantOptions.cs` | `"Qdrant"` |
| `Configuration/PostgresOptions.cs` | `"Postgres"` (lexical search **and** chat history / RAG logging) |
| `Configuration/LlmServiceOptions.cs` | `"LlmService"` |

---

## 5. Qdrant payload contract

The vector store is the integration point between the indexer and this
service. Both sides must agree on the payload keys. The fields this service
reads (with the aliases it tolerates in `QdrantVectorSearchService.MapPayload`):

| Logical field | Primary key | Aliases | Type | Purpose |
|---|---|---|---|---|
| Chunk text | `text` | `chunkText` | string | Clean chunk text used as RAG context. |
| Chunk id | `chunkId` | — | string | Stable id (e.g. `WikiJs:265:0`). |
| Order | `chunkOrder` | — | int (accepted as int / numeric string) | 0-based position of the chunk within its document. **Critical** for context assembly. |
| Source system | `sourceSystem` | — | string | E.g. `WikiJs`. Used in the scroll filter. |
| Document id | `sourceDocumentId` | `documentId` | string | Document id within the source system. **Critical** for selection + expansion. |
| URL | `sourceUrl` | `url`, `path` | string | Displayed to the user. |
| Title | `title` | `pageTitle`, `documentTitle` | string | Document title. |

Every other payload key is also captured into `RetrievedChunk.Metadata` as a
string for debugging/inspection.

The producer side (canonical writer of these keys) is
`Use.Indexing.Worker/Persistence/QdrantVectorStore.cs`. If you rename a key
there, update the alias list in `QdrantVectorSearchService.MapPayload` here.
See also `Use.Indexing.Worker/README.md` §9.3 for the full producer contract.

### 5.1 PostgreSQL lexical store contract

The lexical pass reads the chunk store written by
`Use.Indexing.Worker/Persistence/Postgres/PostgresIndexRepository.cs`
(canonical DDL: `../use-sql-db/create.sql`). The query
(`PostgresLexicalSearchService`) joins two tables:

| Table | Columns this service reads |
|---|---|
| `rag_document_chunks c` | `chunk_id`, `source_system`, `source_document_id`, `chunk_order`, `heading_path`, `text`, `search_vector` (GIN-indexed `tsvector`, generated from `search_text`). |
| `rag_source_documents d` | `title`, `source_url` (joined via `d.id = c.source_document_ref_id`). |

Ranking uses `ts_rank_cd(c.search_vector, websearch_to_tsquery('simple', @query))`.
The `'simple'` text-search config is intentional — it is safer for
Slovak/Czech and internal product names than `'english'` (which stems and
applies English stop-words). Lexical scores are used **for ranking only**; the
orchestrator fuses by rank (RRF), never by the raw `ts_rank_cd` value.

---

## 6. Configuration reference (`appsettings.json`)

```jsonc
{
  "LlmService": {
    "BaseUrl": "http://localhost:5133",
    "EmbeddingEndpoint": "/api/embeddings",
    "GenerationEndpoint": "/api/chat",
    "RerankEndpoint": "/api/rerank",
    "EmbeddingTimeout": "00:02:00",
    "GenerationTimeout": "00:05:00",
    "RerankTimeout": "00:02:00"
  },
  "Qdrant": {
    "Host": "localhost",
    "Port": 6334,
    "UseHttps": false,
    "CollectionName": "documentation_chunks",
    "TopK": 20            // legacy; not used by the new pipeline
  },
  "Rag": {
    "RetrievalMode": "Hybrid",   // SemanticOnly | LexicalOnly | Hybrid
    "InitialTopK": 40,           // chunks pulled by the semantic (vector) pass
    "LexicalTopK": 40,           // chunks pulled by the lexical (PostgreSQL) pass
    "TopDocuments": 3,           // documents kept after fusion + ranking
    "RrfK": 60,                  // Reciprocal Rank Fusion constant (k)
    "MaxChunksPerDocument": 500, // safety cap for the per-document scroll
    "MaxChunkCharacters": 3000,  // hard truncation per chunk in the prompt
    "RerankingEnabled": true,    // rerank top fused chunks before selection
    "RerankTopK": 30             // how many fused chunks are sent to the reranker
  },
  "Postgres": {
    "Enabled": true,
    "ConnectionString": "Host=localhost;Port=5432;Database=use_metadata_db;Username=use_app_user;Password=local_dev_password"
  }
}
```

All values can be overridden by environment variables using the standard
`Section__Key` convention (e.g. `Rag__RetrievalMode=SemanticOnly`,
`Rag__RrfK=40`, `Postgres__Enabled=false`,
`Postgres__ConnectionString=…`, `Qdrant__ApiKey=…`). Secrets (the Postgres
password, Qdrant API key, future JWT signing keys) **should** come from
environment / user-secrets in real deployments, never from committed config —
the connection string above is a local-dev convenience.

### 6.1 Tuning guidelines

| Knob | What it controls | When to raise | When to lower |
|---|---|---|---|
| `Rag:RetrievalMode` | Which passes run (`SemanticOnly` / `LexicalOnly` / `Hybrid`). | Use `Hybrid` normally; `LexicalOnly` / `SemanticOnly` to isolate a pass during evaluation. | — |
| `Rag:InitialTopK` | Width of the semantic candidate pool. | Recall feels low; correct document never makes it into the candidate set. | Latency of the initial search becomes a problem. |
| `Rag:LexicalTopK` | Width of the lexical candidate pool. | Keyword/exact-token matches are being missed. | Lexical noise is dominating fusion. |
| `Rag:RrfK` | Flatness of the rank→score curve. Higher = ranks matter less. | Top ranks dominate too aggressively. | You want the very top hits to dominate more. |
| `Rag:TopDocuments` | How many documents the model sees in full. | Answers feel narrow / miss cross-document context. | LLM context window starts overflowing. |
| `Rag:RerankingEnabled` | Whether fused chunks are reranked by the cross-encoder before selection. | Keep `true` for best precision. | Set `false` to isolate fusion behaviour or when the reranker service is unavailable. |
| `Rag:RerankTopK` | How many top fused chunks are sent to the reranker. | Relevant chunks sit just outside the reranked window. | Reranker latency becomes a problem. |
| `Rag:MaxChunksPerDocument` | Safety cap on per-document scroll. | Documents in your corpus exceed 500 chunks. | Rarely useful to lower; it's a safety net. |
| `Rag:MaxChunkCharacters` | Per-chunk truncation in the prompt. | You see chunks getting cut mid-sentence. | Prompt sizes blow past the local LLM context. |

> **Important.** `Qdrant:TopK` is **legacy** and no longer consulted by the
> orchestrator. `Rag:InitialTopK` is the only knob that drives the
> similarity pass today.

---

## 7. Prompt policy

### 7.1 System prompt (Slovak, fixed)

The system prompt sets these non-negotiable rules:

1. Answer in Slovak unless the user explicitly asks otherwise.
2. **Only** use the documentation context attached to the user prompt.
3. If the context is insufficient, say so plainly — do not improvise.
4. Never invent facts, links, numbers, or command names.
5. For module/function/command questions: summarize first, then list concrete
   points from the documentation.
6. End the answer with the source documents / URLs used.

The exact text lives in `RagPromptBuilder.SystemPromptText`. Treat it as
part of the public contract: changing it changes the evaluation baseline.

### 7.2 User prompt shape

```
Kontext:
=== Dokument 1: <title> ===
URL: <url>
<chunk 1 text>

<chunk 2 text>
...

=== Dokument 2: <title> ===
URL: <url>
<chunk 1 text>
...

Užívateľská otázka:
<question>

Odpoveď:
```

This shape was chosen so the model perceives each document as a single
coherent unit, in original reading order — which is the whole point of the
"top-3 documents in full" retrieval policy.

---

## 8. Embedding + generation (`Services/Embeddings/LlmServiceClient.cs`)

This service does not call Ollama directly. It calls
**`Use.LlmService.Api`**, which in turn calls a local Ollama-hosted model.
That keeps model selection and prompt boilerplate in one place across the
whole system.

### 8.1 Wire format

**Embedding** — `POST {LlmService:BaseUrl}/api/embeddings`

Request:
```json
{ "input": "<user question>", "sourceType": "UserQuery" }
```

Response:
```json
{ "model": "embeddinggemma", "dimensions": 768, "embedding": [ … ] }
```

**Generation** — `POST {LlmService:BaseUrl}/api/chat`

Request:
```json
{ "prompt": "<grouped context + question>", "systemPrompt": "<rules>" }
```

Response:
```json
{ "answer": "…", /* + any model metadata exposed by the LLM API */ }
```

**Rerank** — `POST {LlmService:BaseUrl}/api/rerank`

Request:
```json
{
  "query": "<user question>",
  "documents": [ { "chunkId": "WikiJs:265:7", "text": "<candidate chunk text>" } ]
}
```

Response (sorted by score desc):
```json
{
  "model": "BAAI/bge-reranker-v2-m3",
  "results": [ { "chunkId": "WikiJs:265:7", "score": 0.934 } ]
}
```

### 8.2 Concurrency & timeouts

- Per-request: one embedding call + one rerank call (Hybrid/Lexical, when
  `Rag:RerankingEnabled=true`) + one generation call.
- The typed `HttpClient` (built by `IHttpClientFactory`) uses the **largest**
  of `EmbeddingTimeout`, `GenerationTimeout` and `RerankTimeout` as the global
  socket timeout; per-call cancellation tokens still apply.
- No retries are performed at this layer — the controller maps failures to
  `502 Bad Gateway` and lets the caller decide.

### 8.3 Embedding model

To stay consistent with the indexer, **the question must be embedded with
the same model that produced the corpus vectors** (currently
`embeddinggemma`, 768 dimensions, Cosine distance). The application service
does not enforce this — it trusts the LLM API to be configured the same way
as the indexer. If they drift, similarity scores become meaningless and the
selector silently degrades. See `Use.Indexing.Worker/README.md` §8 / §13.

---

## 9. Vector search (`Services/VectorSearch/QdrantVectorSearchService.cs`)

Qdrant is reached over **gRPC on port 6334** (preferred by `Qdrant.Client`).
HTTP port 6333 is still useful from `curl` / Qdrant UI for inspection.

### 9.1 Two operations, one collection

| Method | Qdrant call | Used by |
|---|---|---|
| `SearchAsync` | `_client.SearchAsync(collection, vector, filter, limit, payload=true)` | Stage 2 — initial similarity pass. |
| `ListByDocumentAsync` | `_client.ScrollAsync(collection, filter, pageSize=256, offset, payload=true)` paged until exhausted or `MaxChunksPerDocument` | Stage 4 — pull every chunk of a chosen document. |

Both share the same `MapPayload` routine, which is tolerant of legacy /
alternative key names (see §5).

### 9.2 Lifetime

`QdrantVectorSearchService` is registered as a **singleton** because it owns
the long-lived gRPC channel inside `QdrantClient`. It implements
`IAsyncDisposable`, so the host will close the channel cleanly on shutdown.

---

## 9b. Lexical search (`Services/LexicalSearch/PostgresLexicalSearchService.cs`)

PostgreSQL is reached over the standard wire protocol via **Npgsql** (direct
SQL, no EF Core — matching `Use.Indexing.Worker`'s repository style).

### 9b.1 One operation

| Method | SQL | Used by |
|---|---|---|
| `SearchAsync` | `SELECT … ts_rank_cd(search_vector, websearch_to_tsquery('simple', @query)) … WHERE search_vector @@ websearch_to_tsquery(…) ORDER BY score DESC LIMIT @limit` | Stage 2b — lexical pass. |

- `Enabled` is `false` when `Postgres:Enabled=false` or the connection string
  is empty; `SearchAsync` then returns an empty list (and the orchestrator
  degrades / fails per §3.0).
- `websearch_to_tsquery` is robust to arbitrary user input; a `plainto_tsquery`
  fallback is used if it ever errors.

### 9b.2 Lifetime

`PostgresLexicalSearchService` is registered as a **singleton** but no longer
owns a connection pool: it borrows the shared `NpgsqlDataSource` from
`IPostgresDataSourceProvider` (also a singleton, disposed on shutdown via
`IAsyncDisposable`). The same pool backs the chat-history / RAG-logging
repositories, so there is **one** Postgres connection pool / connection string
for the whole service.

---

## 9c. Hybrid retrieval + RRF (`Services/Rag/Retrieval/HybridRetrievalService.cs`)

Runs the semantic and lexical passes **in parallel** (`Task.WhenAll`) and merges
them with Reciprocal Rank Fusion (see §3.3). Returns a ranked
`IReadOnlyList<FusedChunkResult>`. Registered as **scoped** (it logs per-request
and only depends on singletons). It is bypassed entirely in `SemanticOnly` mode.

---

## 10. Response contract

`ChatResponse` returns three fields:

- **`Answer`** — the model's generated text (or the fallback string when no
  documentation was found).
- **`Sources`** — one `SourceReference` per **document** (title, URL,
  `sourceSystem`, representative `ChunkId`, `Score`). `ChunkId` is the
  document's top fused chunk (`RepresentativeChunkId`); `Score` is the
  **document score** in hybrid/lexical mode (or the best similarity score in
  semantic-only mode). This is what a UI should render as citations.
  - On the fallback "raw chunks" path (§3.7), there is one `SourceReference`
    per chunk instead. The orchestrator log will say so.
- **`RetrievedChunks`** — the full flat list of ordered chunks that were
  sent to the LLM. Useful for debugging and for richer UIs that want to
  render inline evidence (or for evaluation harnesses that need ground-truth
  comparisons against retrieval).

---

## 11. Dependency Injection (`Program.cs`)

```csharp
builder.Services.AddHttpContextAccessor();
builder.Services.AddHttpClient<ILlmServiceClient, LlmServiceClient>(/* base url + timeouts */);

builder.Services.AddSingleton<IVectorSearchService, QdrantVectorSearchService>();

// Shared Postgres pool (single NpgsqlDataSource) for lexical search + chat history.
builder.Services.AddSingleton<IPostgresDataSourceProvider, PostgresDataSourceProvider>();
builder.Services.AddSingleton<ILexicalSearchService, PostgresLexicalSearchService>();

builder.Services.AddScoped<IHybridRetrievalService, HybridRetrievalService>();
builder.Services.AddSingleton<IDocumentSelector, TopDocumentSelector>();
builder.Services.AddSingleton<IHybridDocumentSelector, HybridDocumentSelector>();
builder.Services.AddScoped<IContextAssembler, OrderedDocumentContextAssembler>();
builder.Services.AddSingleton<IPromptBuilder, RagPromptBuilder>();
builder.Services.AddScoped<IRagOrchestrator, RagOrchestrator>();

// Chat history + RAG logging (direct SQL, shared pool).
builder.Services.AddScoped<IAppUserRepository, AppUserRepository>();
builder.Services.AddScoped<IChatSessionRepository, ChatSessionRepository>();
builder.Services.AddScoped<IChatMessageRepository, ChatMessageRepository>();
builder.Services.AddScoped<IRagQueryLogRepository, RagQueryLogRepository>();
builder.Services.AddScoped<IRagRetrievedChunkLogRepository, RagRetrievedChunkLogRepository>();
builder.Services.AddScoped<IChunkReferenceResolver, ChunkReferenceResolver>();
builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();
builder.Services.AddScoped<IChatApplicationService, ChatApplicationService>();
```

- `IVectorSearchService` is a singleton — long-lived gRPC channel.
- `IPostgresDataSourceProvider` is a singleton — it **owns the one shared
  `NpgsqlDataSource` pool** and disposes it on shutdown (`IAsyncDisposable`).
  Both `ILexicalSearchService` and the chat/logging repositories borrow this
  pool, so there is a single connection pool / connection string for the whole
  service. It is a no-op when Postgres is disabled.
- `ILexicalSearchService` is a singleton (stateless over the shared pool).
- The chat/logging **repositories**, `IChunkReferenceResolver`,
  `ICurrentUserService` and `IChatApplicationService` are **scoped**
  (per-request; the user service reads `HttpContext`).
- `IHybridRetrievalService` is scoped (per-request logging; depends only on
  singletons).
- `IDocumentSelector`, `IHybridDocumentSelector` and `IPromptBuilder` are
  pure / stateless → singleton.
- `IContextAssembler` is scoped because it logs per-request and may later
  carry per-request caches.
- `IRagOrchestrator` is scoped so it can later pick up `HttpContext`-bound
  identity / permission services.

> Both Qdrant and Postgres connection objects are owned by their respective
> singletons and disposed via `IAsyncDisposable` on host shutdown.

---

## 11b. Chat history, identity & RAG logging (SQL-backed)

This service persists conversations and per-request RAG telemetry to the **same
PostgreSQL database** used for lexical search (one connection string, one pooled
`NpgsqlDataSource`). The whole feature is orchestrated by
`Services/Chat/ChatApplicationService.cs`, keeping `ChatController` thin and the
RAG pipeline (`RagOrchestrator`) free of persistence concerns.

### 11b.1 Tables

| Table | Holds | Written by |
|---|---|---|
| `app_user` | One row per user (Entra-ready: `entra_object_id` + `tenant_id`, `email`, `display_name`, `last_login_at`). **No passwords.** | `AppUserRepository` (get-or-create upsert) |
| `chat_session` | One conversation thread per row, owned by `app_user`. | `ChatSessionRepository` |
| `chat_message` | One message per row (`role` ∈ `user`/`assistant`/`system`). | `ChatMessageRepository` |
| `rag_query_log` | One row per RAG execution (timings, counts, status, rating). `chat_message_id` → the **user-question** message. | `RagQueryLogRepository` |
| `rag_retrieved_chunk_log` | Top retrieved candidates per query (rank, scores, selected flag). FKs are SQL UUIDs. | `RagRetrievedChunkLogRepository` |

DDL: `../use-sql-db/create.sql`. Data access is **direct SQL via Npgsql** (no EF
Core), matching `PostgresLexicalSearchService` / the indexing worker.

### 11b.2 Identity (no real auth yet)

**Microsoft Entra ID / JWT is not implemented.** `ICurrentUserService` is the
single seam where it will plug in later. Resolution order today:

1. **Dev identity headers** (preferred):
   `X-Entra-Object-Id`, `X-Tenant-Id`, `X-User-Email`, `X-User-Name`.
2. **Development fallback** (no headers, `ASPNETCORE_ENVIRONMENT=Development`):
   a deterministic local dev user — `local.dev@use.local` / `Local Dev User` /
   `local-dev-user` / `local-dev-tenant`.
3. **Otherwise** (no identity, non-Development): `UnauthorizedAccessException`
   → `401`.

The resolved identity is upserted into `app_user` (an `INSERT … ON CONFLICT
(tenant_id, entra_object_id) DO UPDATE` that also refreshes `last_login_at` and
the profile fields), and the row is returned as an `AppUser`.

> **TODO (auth):** replace header parsing with claims read from the validated
> Entra ID JWT on `HttpContext.User` (`oid`, `tid`, `preferred_username`/`email`,
> `name`). Nothing downstream needs to change — it all depends on
> `ICurrentUserService`.

### 11b.3 `POST /api/chat` persistence flow

1. Resolve/create the current `AppUser`.
2. Resolve the session: continue `chatSessionId` (verifying ownership → `404` if
   not owned) **or** create a new `chat_session` titled from the first ~60 chars
   of the question (else `"New chat"`).
3. Insert the **user** `chat_message`.
4. Insert the initial `rag_query_log` (status defaults to `completed`,
   `started_at = now()`).
5. Run `IRagOrchestrator.ExecuteAsync` (RAG pipeline unchanged), timing the call.
6. Insert the **assistant** `chat_message`; `updated_at = now()` on the session.
7. Update the `rag_query_log` with timings/counts/status (§11b.4).
8. Best-effort insert `rag_retrieved_chunk_log` rows (§11b.5).
9. Return `ChatResponse` enriched with `chatSessionId`, `userMessageId`,
   `assistantMessageId`, `ragQueryLogId`.

On **failure** the log is marked `failed` (+ `error_message`); on
**cancellation**, `cancelled`. Both use `CancellationToken.None` for the update
(the request token may already be cancelled) and the original exception still
bubbles to the controller (`502`).

### 11b.4 What RAG telemetry is logged

The orchestrator now returns `RagExecutionResult` (answer + telemetry), so the
following are populated:

| Column | Value | Notes |
|---|---|---|
| `original_query` | the user question | |
| `retrieval_strategy` | `semantic` / `lexical` / `hybrid_rrf` | the **resolved** mode (after the Postgres degrade policy) |
| `started_at` / `completed_at` | DB `now()` at create / update | |
| `duration_ms` | total wall-clock around `ExecuteAsync` (measured in the chat service) | |
| `retrieval_duration_ms` | retrieval + fusion + rerank (Stopwatch in the orchestrator) | best-effort |
| `generation_duration_ms` | the LLM generation call only | best-effort; `NULL` when no generation ran (empty answer) |
| `total_retrieved_chunks` | fused candidate count (or similarity-hit count in `SemanticOnly`) before document selection | |
| `selected_context_chunks` | `ChatResponse.RetrievedChunks.Count` (chunks fed to the LLM after document expansion) | |
| `answer_status` | `completed` / `failed` / `cancelled` / `no_relevant_context` | |
| `error_message` | exception message on `failed` | |

**Left `NULL` / default on purpose:**

- `augmented_query` — the pipeline does **not** rewrite/expand the query yet
  (no HyDE / rewriting), so this is always `NULL`. Wire it up when query
  rewriting lands.
- `user_rating` / `user_feedback` — set later via the rating endpoints.

### 11b.5 Retrieved-chunk logging (stable id → SQL UUID)

`rag_retrieved_chunk_log.document_id` / `chunk_id` are the **internal SQL UUIDs**
(`rag_source_documents.id` / `rag_document_chunks.id`), **not** the stable text
chunk id (`WikiJs:265:7`). The retrieval models only carry the stable text id, so
`IChunkReferenceResolver` resolves them in one set-based query
(`WHERE chunk_id = ANY(@ids)`), reading `rag_document_chunks.id` +
`source_document_ref_id`.

- **Candidates logged = the top ≤ 30 retrieved candidates _before_ document
  expansion** — the fused + reranked chunks on the Hybrid/Lexical path, or the
  raw similarity hits on the `SemanticOnly` path. This is the most useful signal
  for debugging bad answers ("what did retrieval actually surface, and at what
  rank/score?"). Each row stores `rank`, `semantic_score`, `lexical_score`,
  `rrf_score`, and `was_selected_for_context` (true when that candidate's chunk
  ended up in the LLM context after document expansion).
- `semantic_score` / `lexical_score` are `NULL` when the chunk was not a hit in
  that pass; `rrf_score` is `NULL` on the `SemanticOnly` path (no fusion).
- **Unresolved chunks are skipped with a warning** (e.g. a Qdrant-only chunk not
  present in the SQL store) — never a failure.
- The whole step is **best-effort**: any error is logged and swallowed so a
  logging hiccup never fails an already-produced answer.

### 11b.6 Rating / feedback

Two endpoints write `user_rating` (`-1` bad / `1` good / `null`) +
`user_feedback`, both **scoped to the current user** (the `UPDATE` joins
`rag_query_log → chat_message → chat_session` and matches the owner):

- `POST /api/chat/rag-query-logs/{ragQueryLogId}/rating` — **preferred**; the
  chat response returns `ragQueryLogId` directly.
- `POST /api/chat/messages/{messageId}/rating` — resolves a `rag_query_log` from
  a `chat_message` id.

> **Schema limitation (documented).** `rag_query_log` references only the
> **user-question** message (`chat_message_id`), not the assistant answer. The
> frontend typically holds the *assistant* message id. So
> `ResolveLogIdByMessageAsync` first tries a direct match (the id **is** the user
> message), and otherwise treats the id as the assistant answer and falls back to
> the **most recent preceding `user` message in the same session**, then looks up
> its log. This is correct for the normal one-question→one-answer flow but is a
> heuristic. **Adding an `assistant_message_id` column to `rag_query_log` would
> make this a direct, unambiguous lookup** — recommended, but the schema is **not**
> modified automatically here.

### 11b.7 Authorization-ready behavior

- Every session/message/log read or rating is scoped by `userId`; user A can
  never load or rate user B's data (SQL `WHERE user_id = …` / ownership joins).
- Not-found / not-owned resolves to `404` consistently.
- Source/document **permission filtering is still TODO** — all sources are
  visible to everyone for now (no per-document ACL on retrieval yet).

### 11b.8 Postgres-disabled behavior

- `POST /api/chat` still answers when `Postgres:Enabled=false` (it runs the
  pipeline and returns the answer with the chat-metadata fields `null`) — local
  dev / `SemanticOnly` keeps working.
- The history/rating endpoints require persistence and return
  `503 Service Unavailable` when Postgres is disabled.

### 11b.9 Retention

No deletion / retention / pruning policy is implemented for
`rag_retrieved_chunk_log` (or any log table) yet — rows accumulate. Pruning is a
future task (see §16).

---

## 12. Running locally

1. **Qdrant** — `docker compose -f ../use-qdrant-db/docker-compose.yml up -d`.
   Verify with `curl http://localhost:6333/collections`.
2. **PostgreSQL** — `docker compose -f ../use-sql-db/docker-compose.yml up -d`.
   The lexical store (`use_metadata_db`, tables `rag_source_documents` /
   `rag_document_chunks`) is populated by the indexing worker. Required for
   `Hybrid` / `LexicalOnly`; not needed for `SemanticOnly`.
3. **Use.LlmService.Api** — `dotnet run` in `../Use.LlmService.Api/`. It
   should listen on `http://localhost:5133` and connect to a local Ollama
   running `embeddinggemma` (embeddings) and the chat/generation model.
4. **Use.Reranker.Service** — start the Python reranker (only needed when
   `Rag:RerankingEnabled=true`):
   ```bash
   cd ../Use.Reranker.Service
   pip install -r requirements.txt
   uvicorn main:app --host 0.0.0.0 --port 8000
   # or: docker compose up -d
   ```
   `Use.LlmService.Api` reaches it at `Reranker:BaseUrl` (default
   `http://localhost:8000`). Set `Rag:RerankingEnabled=false` to run without it.
5. **Use.Indexing.Worker** — `dotnet run` in `../Use.Indexing.Worker/`.
   After one cycle the Qdrant collection `documentation_chunks` **and** the
   PostgreSQL chunk store are populated. See `Use.Indexing.Worker/README.md`.
6. **Application service** — `dotnet run` here. Swagger UI is exposed in
   `Development` at `/swagger`. Example call:

```bash
curl -s -X POST http://localhost:5xxx/api/chat \
  -H 'Content-Type: application/json' \
  -d '{"question":"Kedy je naplánovaný update systému Humanet pre mesiac August?"}' | jq
```

You can also test the reranker layers in isolation:

```bash
# 1) Python reranker directly
curl -X POST http://localhost:8000/rerank \
  -H "Content-Type: application/json" \
  -d '{
    "query": "Kedy je naplánovaný update systému Humanet pre mesiac August?",
    "documents": [
      { "chunkId": "1", "text": "Mesiac: August; Dátum pre update: 22. 8. 2025." },
      { "chunkId": "2", "text": "Táto stránka popisuje používateľské oprávnenia." }
    ]
  }'

# 2) LlmService.Api gateway rerank endpoint
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

## 13. Numbers you actually care about (for evaluation)

These are the values that define the **current evaluation baseline**. Bump
them in this table and in `appsettings.json` together whenever you change a
parameter that affects retrieval quality.

| Question | Today's answer |
|---|---|
| Retrieval mode | `Rag:RetrievalMode` = **Hybrid** (semantic + lexical, RRF) |
| Embedding model used for queries | `embeddinggemma` (via Ollama, behind `Use.LlmService.Api`) — must match the indexer |
| Vector dimensions per query | **768** floats |
| Distance metric in Qdrant | Cosine |
| Stage-2a semantic candidate width | `Rag:InitialTopK` = **40** chunks |
| Stage-2b lexical candidate width | `Rag:LexicalTopK` = **40** chunks |
| Stage-2b lexical store | PostgreSQL FTS over `rag_document_chunks` (`ts_rank_cd`, `'simple'`) — BM25-ready, not yet true BM25 |
| Stage-2c fusion | Reciprocal Rank Fusion, `Rag:RrfK` = **60** |
| Reranker between stages | **BAAI/bge-reranker-v2-m3** (cross-encoder, normalized scores) |
| Reranking position | After RRF fusion, before document selection |
| Reranking toggle | `Rag:RerankingEnabled` = **true** |
| Reranker candidate width | `Rag:RerankTopK` = **30** top fused chunks |
| Reranker endpoint | `Use.LlmService.Api /api/rerank` → `Use.Reranker.Service /rerank` |
| Stage-3 document selector | `HybridDocumentSelector` — rerank-driven `DocumentScore = bestRerank + top-3 rerank sum + hitCount*0.01` when reranked, else fused `best + top-3 sum + hitCount*0.01`; `TopDocumentSelector` (hit-count) in `SemanticOnly` |
| Stage-3 documents kept | `Rag:TopDocuments` = **3** documents |
| Stage-4 source of full document text | Qdrant scroll over `(sourceSystem, sourceDocumentId)` — **not** Wiki.js API |
| Stage-4 per-document chunk cap | `Rag:MaxChunksPerDocument` = **500** |
| Stage-5 chunk truncation | `Rag:MaxChunkCharacters` = **3000** chars |
| Stage-5 prompt shape | Slovak system prompt + chunks grouped by document, ordered by `chunkOrder` |
| LLM calls per request | 1 embedding (skipped in `LexicalOnly`) + 1 rerank (Hybrid/Lexical, when enabled) + 1 generation |
| Qdrant calls per request | 1 similarity search + N scrolls (N = `TopDocuments`, in parallel) |
| PostgreSQL calls per request | 1 lexical search (Hybrid / LexicalOnly; 0 in SemanticOnly) **+** chat-history / RAG-logging writes (session, 2 messages, query log create+update, chunk-id resolve, retrieved-chunk insert, chunk-resolve lookup) |
| Auth / per-user filtering | **Identity only** — dev/header `ICurrentUserService` (Entra ID **not** implemented); reads scoped per user; **no source/document permission filtering yet** (TODO) |
| Chat history | **Persisted** in PostgreSQL (`app_user` / `chat_session` / `chat_message`); **not yet replayed** into the prompt (TODO) |
| RAG logging | `rag_query_log` (timings/counts/status/rating) + `rag_retrieved_chunk_log` (top ≤30 candidates before expansion). `augmented_query` always `NULL` (no query rewriting yet) |

This is the snapshot the first evaluation report should be tied to. Subsequent
edits to the retrieval policy should be evaluated against this baseline.

---

## 13b. Automated retrieval evaluation mode

An optional, self-contained harness that measures **retrieval quality** against
a fixed dataset of questions with known target sources. It runs the real
pipeline up to — but **not including** — prompt building and LLM generation, so
it scores only retrieval / fusion / reranking / document-selection / context
assembly. It is **off by default** and does not affect normal chat at all.

### What it does

On startup (when enabled) a `RagEvaluationHostedService` (a `BackgroundService`)
loads a JSONL dataset, runs each question through `IRetrievalProbe`
(implemented by `RagOrchestrator` — the same retrieval services as chat, **no
generation**), computes per-stage hit metrics, and writes a JSON + CSV report.

> **Generation is skipped.** The probe stops after context assembly. The
> `IPromptBuilder` and `ILlmServiceClient.GenerateAsync` are never invoked, no
> chat messages are persisted, and no answer rating is performed.
>
> **Normal chat is unchanged.** When `Evaluation:EvaluationModeEnabled=false`
> the harness is completely inert and the service behaves exactly as documented
> in the rest of this file.

### Enabling it (`appsettings.json`)

```jsonc
"Evaluation": {
  "EvaluationModeEnabled": false,          // master switch — true to enable
  "CasesPath": "Evaluation/rag_evaluation_cases.v1.jsonl",
  "OutputDirectory": "Evaluation/Reports",
  "RunOnStartup": true,                     // run automatically on startup
  "MaxCases": null,                         // cap evaluated cases; null = all
  "StopApplicationAfterEvaluation": false   // shut down after the run (CI/batch)
}
```

All keys are overridable via the standard environment-variable convention, e.g.
`Evaluation__EvaluationModeEnabled=true`, `Evaluation__MaxCases=20`.

Relative `CasesPath` / `OutputDirectory` are resolved against the application
**content root** (the project directory in Development).

### Where to put the dataset

```
Use.Application.Service/Evaluation/rag_evaluation_cases.v1.jsonl
```

Reports are written to:

```
Use.Application.Service/Evaluation/Reports/
  rag-evaluation-report-{yyyyMMdd-HHmmss}.json
  rag-evaluation-results-{yyyyMMdd-HHmmss}.csv
```

The dataset is copied next to the binaries on build (`.csproj` `CopyToOutputDirectory`),
so it is also found when the content root is the output directory.

### JSONL schema (one JSON object per line)

```jsonc
{
  "caseId": "humanet-update-august-v1",   // unique, stable id (required)
  "caseGroupId": "humanet-update-august", // group id for paraphrase variants
  "variantId": "v1",
  "enabled": true,                         // false → line is ignored

  "question": "Kedy je naplánovaný update systému Humanet pre mesiac August?", // required
  "language": "sk",

  "questionType": "table_value_lookup",    // concrete_fact_lookup | table_value_lookup | definition_question | procedural_how_to | troubleshooting_question | configuration_question | multi_chunk_single_page | multi_page_synthesis | generic_topic_question | ambiguous_question | unanswerable_in_scope | out_of_scope | exact_identifier_lookup
  "retrievalType": "single_page_single_chunk", // single_page_single_chunk | single_page_multi_chunk | multi_page | page_level_only | negative_no_source_expected | ambiguous_requires_clarification
  "difficulty": "easy",                    // easy | medium | hard
  "answerability": "answerable",           // answerable | unanswerable | ambiguous | out_of_scope

  "expectedBehavior": "answer_from_context", // answer_from_context | refuse_insufficient_context | ask_clarifying_question | refuse_out_of_scope

  "targetSourceSystem": "WikiJs",
  "targetSourceDocumentIds": ["94"],       // document-level expectation
  "targetChunkIds": ["WikiJs:94:2"],       // chunk-level expectation (preferred when present)

  "targetTitle": "Harmonogram pre update systému",
  "targetPath": "humanet/harmonogram",
  "targetUrl": "http://localhost:3000/sk/humanet/harmonogram",

  "expectedAnswerFacts": ["Update systému Humanet pre August je 22. 8. 2025."],
  "acceptableAnswerPatterns": ["22. 8. 2025"],
  "mustNotContain": ["neviem", "dokumentácia neobsahuje"],

  "notes": "Concrete date lookup from a schedule table."
}
```

Loader rules: empty lines are ignored, `enabled:false` cases are skipped,
`caseId` + `question` are required, invalid lines are logged and skipped, and
the run only aborts if **no** valid cases remain. `MaxCases` caps how many
enabled cases run.

### Metrics per case

For each case the harness records hit + best-rank at every stage: `semantic`,
`lexical`, `fusion`, `rerank`, `selectedDocument`, `finalContext`.

- **Matching.** When `targetChunkIds` are present, matching is chunk-level
  (`candidate.chunkId ∈ targetChunkIds`); otherwise it is document-level
  (`candidate.sourceDocumentId ∈ targetSourceDocumentIds`).
- **Document selection** is always matched at document level.
- **Negative / out-of-scope / unanswerable / ambiguous** cases
  (`retrievalType:negative_no_source_expected`, `questionType:out_of_scope`,
  `answerability` ≠ `answerable`, or no targets) are treated as
  **informational** and excluded from the recall denominators.

### Failure-stage diagnosis (answerable cases)

`passed` → `missed_by_semantic` / `missed_by_lexical` → `missed_after_fusion` →
`lost_after_rerank` → `lost_in_document_selection` → `lost_in_context_assembly`,
plus `no_expected_source_defined` (informational) and `evaluation_error`.

### Running it

1. Put the dataset at `Evaluation/rag_evaluation_cases.v1.jsonl`.
2. Set `Evaluation:EvaluationModeEnabled=true` (config or
   `Evaluation__EvaluationModeEnabled=true`). Make sure Qdrant / PostgreSQL /
   `Use.LlmService.Api` are running, exactly as for normal retrieval.
3. Start the Application Service (Rider run config or `dotnet run`). The run
   logs progress (`loaded N cases`, `running case k/N`, pass/fail, summary) and
   prints a compact summary plus the report path.

Console summary shape:

```
RAG Evaluation finished
Cases evaluated: 82 (counted toward recall: 74)
Passed: 54   Failed: 20
Final context recall: 73.2%
Selected document recall: 78.0%
Fusion recall: 86.6%
Rerank recall: 81.7%

Failure stages:
  lost_in_document_selection: 7
  missed_after_fusion: 5
  lost_after_rerank: 3
  lost_in_context_assembly: 2

Report written to:
  .../Evaluation/Reports/rag-evaluation-report-20260614-103000.json
```

The JSON report contains `runId`, `startedAt` / `finishedAt` / `durationMs`,
case counts, the four recall figures, `metricsByQuestionType`,
`metricsByDifficulty`, `failureStageCounts`, and every individual case result.
The CSV is a flat per-case table for spreadsheets.

### Removing the feature

It is fully isolated under `Evaluation/` plus the `RagEvaluationOptions`
section and the DI block in `Program.cs`. `RagOrchestrator` only gains the
`IRetrievalProbe` implementation; deleting the folder, the options class, the
`"Evaluation"` config section, the `Program.cs` registrations, and the
`IRetrievalProbe` members removes it entirely.

---

## 14. Operational notes

- **Latency.** Per question (Hybrid): 1 embedding call + (1 vector search ∥ 1
  lexical search, in parallel) + RRF fusion + N scroll calls (N =
  `TopDocuments`, default 3, run in parallel inside the assembler) + 1
  generation call. Generation dominates. `SemanticOnly` skips the lexical
  call; `LexicalOnly` skips the embedding + vector call.
- **Logging.** The orchestrator logs the resolved `RetrievalMode` and the
  selected document list (with hit count / best score / document score).
  `HybridRetrievalService` logs the semantic / lexical / fused counts at
  `Information` and the top fused chunks (chunk id, semantic rank, lexical
  rank, fused score) at `Debug`, and warns when the lexical pass is skipped
  because Postgres is disabled. Failures from Qdrant, PostgreSQL or the LLM
  API are logged at `Error` with the relevant parameters before rethrow.
- **Failure modes.**
  - LLM service unreachable → `502 Bad Gateway` from the controller.
  - Qdrant unreachable → same.
  - PostgreSQL unreachable in `Hybrid`/`LexicalOnly` → same (`502`).
  - `Hybrid`/`LexicalOnly` requested but `Postgres:Enabled=false` → degrade to
    `SemanticOnly` in Development (warning); fail with `502` otherwise.
  - Document deleted between similarity and scroll → that document drops
    out silently; the remaining documents still serve the answer.
- **Determinism.** Given identical Qdrant + PostgreSQL state and identical
  embedding output, the pipeline is deterministic up to the LLM's own
  sampling. The retriever, selector and assembler all use stable secondary
  sorts.

---

## 15. Extension points

The pipeline is intentionally easy to extend:

- **Different vector store** → implement `IVectorSearchService`. Both
  `SearchAsync` and `ListByDocumentAsync` must be supported (the latter is
  what makes whole-document expansion possible).
- **Different lexical store / true BM25** → implement `ILexicalSearchService`
  (e.g. a real BM25 ranker, OpenSearch/Elasticsearch, or a `pg_search`/BM25
  Postgres extension). The chunk store already persists `search_text`, so the
  ranking can be upgraded without re-chunking.
- **Different fusion strategy** → implement `IHybridRetrievalService` (e.g.
  weighted RRF, score normalization, or a learned fusion).
- **Smarter ranking** (e.g. cross-encoder reranker between fusion and
  selection) → implement `IHybridDocumentSelector`. Input is the fused chunk
  list, output is a ranked list of `DocumentSelection` records.
- **Alternative context source** (e.g. fetch the document from the Wiki.js
  API instead of Qdrant scroll, or merge from multiple stores) → implement
  `IContextAssembler` and keep using the same `DocumentSelection` input.
- **Different prompt style or language** → implement `IPromptBuilder`. The
  orchestrator never inspects the prompt text.
- **New input modalities** (e.g. conversation history) → extend
  `ChatRequest` and pass through `IRagOrchestrator.AnswerAsync`.

---

## 16. TODO / known gaps

- Replace PostgreSQL `ts_rank_cd` with a **true BM25** ranker (the chunk store
  is already BM25-ready via `search_text` / `search_vector`).
- **Implement Microsoft Entra ID authentication.** Validate the JWT and replace
  the dev/header identity in `CurrentUserService` with token claims (`oid`,
  `tid`, `email`/`preferred_username`, `name`) read from `HttpContext.User`.
- **Real authorization policies** (e.g. `[Authorize]`, role/scope checks) once
  authentication is in place.
- **Source/document permission filtering** — resolve allowed source systems +
  per-document permissions from the SQL metadata DB and translate to filters in
  **both** the Qdrant search (`SearchAsync`/`ListByDocumentAsync`) and the
  lexical query (`PostgresLexicalSearchService` already accepts a `sourceSystem`
  filter). Currently **all sources are visible to everyone**.
- **Feedback mapping:** add an `assistant_message_id` column to `rag_query_log`
  so `POST /api/chat/messages/{messageId}/rating` is a direct lookup instead of
  the "preceding user message" heuristic (§11b.6).
- **Replay chat history into the prompt** — messages are persisted but not yet
  fed to `IPromptBuilder`.
- **Detailed RAG telemetry:** populate `augmented_query` once query
  rewriting/HyDE exists; `retrieval_duration_ms` / `generation_duration_ms` are
  best-effort Stopwatch values today.
- **Retention / pruning** for `rag_retrieved_chunk_log` (and the other log
  tables) — none implemented; rows accumulate.
- Add a `Rag:MaxTotalContextCharacters` cap once we see real-world prompt
  sizes blow past the local LLM context window.
- Consider a cross-encoder reranker between fusion (Stage 2c) and selection
  (Stage 3) for very ambiguous questions.
- Map LLM/Qdrant/PostgreSQL failures to more specific status codes
  (`503 Service Unavailable` with `Retry-After` for the LLM, etc.).
- Validate `Rag:RetrievalMode` vs `Postgres:Enabled` at startup (currently
  resolved per-request) and replace `Qdrant:TopK` (legacy) with a deprecation
  warning.

