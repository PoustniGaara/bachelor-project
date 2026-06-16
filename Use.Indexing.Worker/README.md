# Use.Indexing.Worker — Indexing Pipeline

The indexing worker is a .NET 8 hosted service (`Microsoft.NET.Sdk.Worker`)
that pulls documentation pages from source systems (currently **Wiki.js**),
turns them into clean text, slices them into semantically meaningful chunks,
embeds those chunks via the **`Use.LlmService.Api`** (which proxies a local
**Ollama** model), stores the resulting vectors in a **Qdrant** collection,
and persists the same chunked documents into **PostgreSQL** as a lexical /
BM25-ready full-text store.

This document describes the **current state** of the project end-to-end:
configuration, every pipeline stage, the chunking policy, the embedding flow,
the Qdrant payload contract, and the PostgreSQL lexical store. Keep it in sync
with the code when behavior changes.

---

## 1. High-level pipeline

```
                      ┌───────────────────────────────┐
   Scheduler /        │  IIndexingOrchestrator         │
   CLI / Trigger ───▶│  (Orchestration/                │
                      │   IndexingOrchestrator.cs)     │
                      └────────────┬──────────────────┘
                                   │ for each source connector
                                   ▼
   1. Discover     ── ISourceConnector.DiscoverAsync       (Wiki.js GraphQL list)
   2. Fetch        ── ISourceConnector.FetchAsync          (full page Markdown)
   3. Parse        ── IDocumentParser                      (Markdig → DocumentOutline)
   4. Normalize    ── ITextNormalizer                      (NFC + whitespace per block)
   5. Chunk        ── IChunkingService                     (StructureAwareChunkingService)
   6. Enrich       ── IEmbeddingTextBuilder                (header + content)
   7. Dump (opt.)  ── IChunkDumpWriter                     (chunk-dumps/…)
   8. Embed        ── IEmbeddingService                    (POST /api/embeddings)
   9. Persist      ── IVectorStore + ISqlChunkRepository    (Qdrant vectors + PostgreSQL lexical)
```

Each stage hides behind an interface, so any single piece can be swapped
without touching the orchestrator. See `Program.cs` for the full DI wiring.

---

## 2. Hosted services

| File | Role |
|---|---|
| `HostedServices/IndexingWorker.cs` | Scheduled loop. Sleeps `Indexing:Interval` between cycles, with `Indexing:StartupDelay` before the first one. |
| `HostedServices/ReindexTriggerListener.cs` | Consumes external reindex events from `IReindexTriggerHandler`. |
| `HostedServices/ConsoleCommandListener.cs` | Interactive CLI (e.g. `index`, `index wikijs`, `index wikijs 265`, `search-sql wikijs "query"`). Handy during development. |

All three resolve the same `IIndexingOrchestrator` to actually do work.

---

## 3. Source connectors

Connectors implement `ISourceConnector`:

```csharp
SourceSystemType SourceSystem { get; }
string Name { get; }
IAsyncEnumerable<SourceDocumentReference> DiscoverAsync(DateTimeOffset? since, …);
Task<SourceDocument> FetchAsync(SourceDocumentReference reference, …);
```

### 3.1 Wiki.js connector (`Connectors/WikiJs/`)

- `WikiJsGraphQlClient` — typed HTTP client; talks to `{BaseUrl}{GraphQlEndpoint}`.
- `WikiJsConnector` — implements discovery (list pages, optionally filtered by
  `OnlyPublished`/`SkipPrivate` and `since` timestamp) and per-page fetch
  (full Markdown body + metadata).
- `WikiJsPage` — faithful projection of the Wiki.js GraphQL response. Stays
  a pure DTO: no parsing or normalization logic lives here.

Fetched pages are returned as `SourceDocument` with:
- `RawContent` = Markdown body from Wiki.js,
- `ContentType = "text/markdown"`,
- `Metadata` carrying `title`, `path`, `description`, `tags`, timestamps, etc.

Adding a new source = implement `ISourceConnector` and register it in
`Program.cs`. The orchestrator picks up every registration via
`IEnumerable<ISourceConnector>`.

---

## 4. Parsing (`Parsing/Markdown/`)

### 4.1 `DefaultDocumentParser`

Routes by MIME type:

| ContentType | Path |
|---|---|
| `text/markdown` | `MarkdownDocumentParser` |
| `text/html` | placeholder (TODO: AngleSharp) |
| anything else | plain-text fallback |

### 4.2 `MarkdownDocumentParser`

Uses **Markdig 0.37** (with `UsePipeTables`, `UseGridTables`, `UseAutoLinks`,
`UseEmphasisExtras`) to produce two artifacts in a single AST pass:

1. **Flattened plain text** (used for storage + fallback chunking).
2. **`DocumentOutline`** — hierarchical section tree (`Root → Section* → Block*`).

`OutlineBuilder` walks Markdig nodes and pushes/pops sections by heading level.
Headings become `DocumentSection`s; paragraphs/lists/tables/code/quotes become
`DocumentBlock`s. `MarkdownPlainTextRenderer` converts inline content to plain
text deterministically:

- emphasis is stripped (text preserved),
- links render as `"label (url)"` so URLs survive into the embedding,
- **tables are flattened row-by-row** (`"header: cell; header: cell"`), so
  each row becomes an independently retrievable sentence,
- code blocks keep their internal layout.

`Outline` is attached to `ParsedDocument` (and later `NormalizedDocument`)
and is **the** input to the structure-aware chunker.

---

## 5. Normalization (`Normalization/DefaultTextNormalizer.cs`)

Per block and per whole-document plain text:

1. Unicode normalize to **NFC**.
2. Collapse all whitespace runs to a single space (newlines kept between
   blocks as soft separators for the chunker).
3. Trim leading/trailing whitespace; drop empty blocks and empty sections.
4. Code blocks are preserved as-is — collapsing whitespace there would corrupt
   indentation-sensitive content.

The result is a `NormalizedDocument` carrying `Title`, `PlainText`, `Tags`,
`Metadata`, optional `Permissions`, and the (also normalized) `Outline`.

---

## 6. Chunking

### 6.1 Strategy: **structure-aware**, with merge-small / split-large

The active service is `Chunking/StructureAwareChunkingService.cs`. It walks
the document outline and produces chunks bounded by character size while
respecting structural boundaries.

**Algorithm per section (depth-first):**

1. Concatenate the section's blocks (paragraphs, lists, tables, code, quotes)
   in document order.
2. Prepend a **heading-path breadcrumb** header:
   `"4. Popisy príkazov › Humanet.UpdatePersonRoles › Konfigurácia"` followed
   by a blank line. This is critical — it gives the embedding model the
   topical context of every chunk.
3. **Size classification:**
   - `< MinCharacters` → buffer this section. Continue accumulating adjacent
     sibling sections (sharing an ancestor) until the buffer is big enough,
     then flush.
   - `<= TargetCharacters` → flush the buffer (if any), emit this section as
     a single chunk.
   - `> TargetCharacters` → flush, then split:
     1. **At block boundaries** first (paragraph / list / table / code).
     2. If a single block is still too big, **at sentence boundaries**
        (regex `(?<=[\.\!\?…])\s+(?=[\p{Lu}\p{N}])`).
     3. If a single sentence is still too big, **hard character window**.
        Configurable overlap is re-emitted at the start of each subsequent
        piece, giving the retriever a bridge across the split.
4. **Deep sections** (`Level > MaxHeadingDepth`) are *absorbed* into their
   nearest in-scope ancestor instead of becoming their own chunks. Their
   heading path is trimmed to `MaxHeadingDepth`.

### 6.2 Properties (defaults; see `Configuration/IndexingOptions.cs`)

| Knob | Default | Meaning |
|---|---:|---|
| `Chunking:TargetCharacters` | **1200** | Soft upper bound per chunk. Block/sentence boundaries are preferred over hitting the bound exactly. |
| `Chunking:MinCharacters` | **300** | Sections smaller than this are merged with adjacent siblings to avoid low-signal chunks. |
| `Chunking:Overlap` | **150** | Trailing characters re-emitted at the start of the next chunk when a section had to be split. |
| `Chunking:MaxHeadingDepth` | **4** | Sections deeper than this collapse into their nearest ancestor. |

### 6.3 Chunk metadata (written into every `DocumentChunk.Metadata`)

| Key | Value |
|---|---|
| `title` | Document title |
| `sourceUrl` | Canonical URL of the source document |
| `path` | Wiki.js page path (when present in source metadata) |
| `description` | Wiki.js page description (when non-empty) |
| `chunkOrder` | Zero-based index of the chunk within the document |
| `headingPath` | `›`-joined breadcrumb of the section that produced this chunk |
| `headingLevel` | Deepest heading level represented in the chunk |
| `blockKinds` | Comma-joined sorted set, e.g. `Paragraph,Table` |

Additionally, the chunk id is deterministic:
`"{SourceSystem}:{SourceDocumentId}:{order}"`, e.g. `WikiJs:265:7`.

### 6.4 Fallback chunker

`CharacterWindowChunkingService` is registered alongside the structure-aware
one and used as a fallback whenever a document arrives without an outline
(e.g. a future plain-text source).

---

## 7. Enrichment for embedding (`Embeddings/EmbeddingTextBuilder.cs`)

The chunk emitted by the chunker carries two pieces of text:

- **`Text`** — clean, human-readable. This is the canonical RAG context shown
  to the user and fed back to the LLM at answer-generation time.
- **`EmbeddingText`** — enriched variant produced by `EmbeddingTextBuilder`,
  used **only** as input to the embedding model.

The enriched text format is deterministic; empty fields are skipped:

```
Source system: WikiJs
Document title: Príkazy
Document path: humanet/integrator/prikazy
Heading path: 4. Popisy príkazov › Humanet.UpdatePersonRoles › Konfigurácia
Chunk order: 12

Content:
Parameter: ApiUrl; Typ: string; Predvolená hodnota: povinné; Popis: URL adresa Humanet REST API…
```

This dramatically improves retrieval precision (especially for short
parameter-row chunks) without polluting the text shown to end users.

---

## 8. Embedding (`Embeddings/LlmServiceEmbeddingService.cs`)

The worker does not call Ollama directly. It calls **`Use.LlmService.Api`**,
which in turn calls a local Ollama-hosted model. This keeps model selection
and prompt boilerplate in one place across the whole system.

### 8.1 Wire format

`POST {LlmService:BaseUrl}/api/embeddings`

Request:
```json
{ "input": "<EmbeddingText or Text>", "sourceType": "DocumentChunk" }
```

Response:
```json
{ "model": "embeddinggemma", "dimensions": 768, "embedding": [ … ] }
```

### 8.2 Concurrency & resilience

- One HTTP call per chunk (Ollama's runtime is largely single-threaded).
- Bounded by `SemaphoreSlim(LlmService:MaxParallelism)` — default **4**.
- Transient failures (`HttpRequestException`, `TaskCanceledException`,
  `IOException`) are retried with exponential backoff up to
  `LlmService:MaxRetries` (default **3**).
- The service trusts the response: it uses the *actual* returned
  `dimensions`/`model`, not the configured ones.

### 8.3 Current embedding model

| Setting | Value |
|---|---|
| `Embedding:Provider` | `llm-service` |
| `Embedding:Model` | **`embeddinggemma`** (served by Ollama) |
| `Embedding:Dimensions` | **768** |
| Distance metric used in Qdrant | **Cosine** |

> **Important.** `Qdrant:VectorSize` in `appsettings.json` is currently set
> to `1536` (legacy). The Qdrant store detects a mismatch with the actual
> embedding length on first upsert, logs a warning, and **creates the
> collection with the actual dimensionality (768)**. If a pre-existing
> collection has the wrong size, delete it once and let the worker recreate
> it. Best practice: set `Qdrant:VectorSize` to `768` to match the model.

---

## 9. Vector storage (`Persistence/QdrantVectorStore.cs`)

Qdrant is reached over **gRPC on port 6334** (preferred by `Qdrant.Client`).
HTTP port 6333 is unused by the worker; you can still hit it from
`curl`/Qdrant UI for inspection.

### 9.1 Collection bootstrap

On the first upsert of an indexing cycle:

1. If the collection does **not** exist, it is created with:
   - `size` = actual embedding dimensions (overrides config if they differ),
   - `distance` = `Qdrant:Distance` (default `Cosine`).
2. If the collection exists, nothing changes (no automatic re-creation).

### 9.2 Point ids

Qdrant requires `uint64` or UUID ids. Our chunk ids look like
`"WikiJs:265:7"` (string), so each id is hashed to a **deterministic UUID**
via SHA-256 (first 16 bytes). Same chunk id ⇒ same UUID ⇒ idempotent upserts.

### 9.3 Payload contract

Each point carries the clean text plus all chunker metadata, with a few
structured copies promoted for easy filtering:

| Key | Type | Notes |
|---|---|---|
| `text` | string | **Clean chunk text** — what the RAG service shows / sends back to the LLM. |
| `embeddingText` | string | The enriched text actually used to produce the vector. Diagnostics only — **do not** use as RAG context. |
| `chunkId` | string | Original (pre-hash) chunk id, e.g. `WikiJs:265:7`. |
| `sourceSystem` | string | E.g. `WikiJs`. Used in scroll/delete filters. |
| `sourceDocumentId` | string | Document id within the source system. |
| `sourceUrl` | string | Canonical URL. |
| `chunkOrder` | int | Position within the document. **Critical** for context reassembly in the RAG service. |
| `title`, `path`, `description`, `headingPath`, `headingLevel`, `blockKinds` | strings | Same values as `DocumentChunk.Metadata`. |
| `model` | string | Embedding model reported by the LLM API. |
| `dimensions` | int | Actual vector length. |

This payload is the **integration contract** with `Use.Application.Service`
(see `Use.Application.Service/RAG.md`). If you rename a key here, update the
alias list in `QdrantVectorSearchService.MapPayload` there.

### 9.4 Replace semantics per document

For every document processed, the pipeline:

1. Deletes existing vectors via a payload filter
   (`sourceSystem == X AND sourceDocumentId == Y`).
2. Upserts the freshly produced vectors.

This guarantees the vector store always mirrors the latest source state and
that deleted/empty pages clean up after themselves.

### 9.5 Metadata repository

`Persistence/InMemoryStores.cs` provides `InMemoryIndexRepository`. It tracks
last-indexed timestamps per source system (used for incremental indexing) and
holds chunk records in memory. Durable chunk storage now lives in PostgreSQL
(see §10); the in-memory repository remains the incremental-cursor bookkeeper.

---

## 10. PostgreSQL lexical / BM25 chunk store (`Persistence/Postgres/`)

Qdrant owns the **semantic vectors**. PostgreSQL owns the **lexical** copy of
the same chunks — clean text plus an enriched `search_text` indexed as a
`tsvector` — so the RAG application can later run BM25 / full-text retrieval
alongside (or fused with) vector search. **The same deterministic
`chunk_id` (`"{SourceSystem}:{SourceDocumentId}:{order}"`, e.g. `WikiJs:94:2`)
is stored in both systems**, so results can be joined or deduplicated.

The two stores are fully independent: a Postgres failure does not corrupt
Qdrant and vice-versa. PostgreSQL persistence is optional and gated by
`Indexing:Postgres:Enabled`.

### 10.1 Schema

Two tables, mirroring the chunker's output:

- `rag_source_documents` — one row per source document
  (`UNIQUE (source_system, source_document_id)`).
- `rag_document_chunks` — one row per chunk, FK → `rag_source_documents`
  (`ON DELETE CASCADE`), `UNIQUE (chunk_id)` and
  `UNIQUE (source_system, source_document_id, chunk_order)`.

`search_vector` is a **stored generated column**:
`to_tsvector('simple', search_text)`, indexed with **GIN** for fast full-text
queries. The full DDL is embedded (idempotent, `IF NOT EXISTS`) in
`PostgresIndexRepository` and applied on first use when
`Indexing:Postgres:EnsureSchema = true`. The two metadata GIN indexes are
named distinctly (`ix_rag_chunks_metadata` vs `ix_rag_documents_metadata`) so
both coexist without a name clash; the canonical DDL lives in
`../use-sql-db/create.sql`.

### 10.2 What goes where

`rag_source_documents`:

| Column | Source |
|---|---|
| `source_system` | `SourceSystem.ToString()`, e.g. `WikiJs` |
| `source_document_id` | original id, e.g. `94` |
| `title` | normalized document title |
| `description` / `path` / `source_url` | source metadata (`description`, `path`, `sourceUrl`/`url`) |
| `source_created_at` / `source_updated_at` | source `createdAt` / `updatedAt` (falls back to `LastModified`) |
| `indexed_at` | `now()` |
| `metadata` | JSONB of remaining source metadata (+ tags); no chunk bodies |

`rag_document_chunks`:

| Column | Source |
|---|---|
| `chunk_id` | deterministic id, e.g. `WikiJs:94:2` |
| `source_document_ref_id` | FK to the `rag_source_documents` row |
| `chunk_order` | chunk metadata `chunkOrder` (falls back to `Order`) |
| `heading_path` / `heading_level` | chunk metadata `headingPath` / `headingLevel` |
| `block_kinds` | chunk metadata `blockKinds` (`"Paragraph,Table"`) parsed into `text[]` |
| `text` | **clean chunk text** — the canonical RAG context |
| `search_text` | enriched lexical text (metadata header + content), see §10.3 |
| `permission_scope` / `access_metadata` | `null` / permissions JSON when available |
| `metadata` | JSONB of remaining chunk metadata (column-mapped keys excluded) |

### 10.3 `search_text` (lexical enrichment)

Built by `Persistence/Postgres/SearchTextBuilder.cs`, mirroring the embedding
text but **without** any embedding-specific fields, so important words that
live in metadata (title, path, description, heading) are searchable too:

```
Source system: WikiJs
Document title: ...
Document path: ...
Document description: ...
Heading path: ...
Chunk order: ...

Content:
<clean chunk text>
```

This lets a BM25-style query like *“Humanet August update”* match a chunk even
when "Humanet" only appears in the document title/path.

### 10.4 Replace semantics per document

`ISqlChunkRepository.ReplaceDocumentAsync` mirrors the Qdrant delete-then-upsert
inside a **single transaction**:

1. `INSERT … ON CONFLICT (source_system, source_document_id) DO UPDATE` the
   document row, `RETURNING id`.
2. `DELETE FROM rag_document_chunks` for that `source_system` + `source_document_id`.
3. `INSERT` the fresh chunks.
4. `COMMIT` (or `ROLLBACK` on any failure).

Reindexing the same page therefore never duplicates documents or chunks, and
the final chunk rows exactly match the latest chunker output. An empty-chunk
document keeps its row but clears its chunks (mirrors stale-vector cleanup).

### 10.5 Configuration (`Indexing:Postgres`)

```jsonc
"Postgres": {
  "Enabled": true,
  "EnsureSchema": true,
  "ConnectionString": "Host=localhost;Port=5432;Database=use_metadata_db;Username=use_app_user;Password=local_dev_password"
}
```

- The connection string carries a password, so — like the Wiki.js token — it
  lives in `appsettings.Development.json` / user-secrets, **not** in committed
  `appsettings.json`. Override anywhere via the standard env convention:
  `Indexing__Postgres__ConnectionString`, `Indexing__Postgres__Enabled`.
- Local docker compose (`../use-sql-db/docker-compose.yml`):
  `postgres:17-alpine`, db `use_metadata_db`, user `use_app_user`,
  password `local_dev_password`, port `5432`.
- **Error handling:** when disabled, indexing behaves exactly as before
  (Qdrant only). When enabled but unreachable/misconfigured, the failure is
  logged and the document's indexing cycle fails loudly (the transaction rolls
  back) — Postgres failures are **not** silently ignored.

### 10.6 Lexical search (testing now, RAG later)

`ISqlChunkRepository.SearchAsync(sourceSystem, query, limit, ct)` runs:

```sql
SELECT c.chunk_id, c.source_system, c.source_document_id, c.chunk_order,
       d.title, c.heading_path, c.text,
       ts_rank_cd(c.search_vector, plainto_tsquery('simple', @query)) AS rank
FROM rag_document_chunks c
JOIN rag_source_documents d ON d.id = c.source_document_ref_id
WHERE c.source_system = @sourceSystem
  AND c.search_vector @@ plainto_tsquery('simple', @query)
ORDER BY rank DESC
LIMIT @limit;
```

Try it from the interactive CLI while the worker runs:

```
search-sql wikijs "Humanet August update"
```

It prints the top chunk ids, title, heading path, rank, and a short text
preview — e.g. the chunk containing
`Mesiac: August; Dátum pre update: 22. 8. 2025`.

---

## 11. Chunk dump (diagnostics)

`Diagnostics/ChunkDumpWriter.cs` writes the exact text that would be sent to
the embedding model to disk, before any embedding/storage happens. Useful for
inspecting chunk quality on real data.

### 11.1 Layout

```
<output>/
└── WikiJs/
    └── Príkazy__265/
        ├── _full.txt                                 # normalized full document + metadata
        ├── chunk_01_of_07_Čo sa v sekcii dozvieme.txt
        ├── chunk_02_of_07_Charakteristika príkazu.txt
        └── …
```

Each chunk file contains a header (id, order, character count, all metadata),
then both the **embedding input** and the **clean RAG text** in clearly
labeled sections so you can compare them side-by-side.

### 11.2 Configuration (`Indexing:ChunkDump`)

| Knob | Default | Meaning |
|---|---|---|
| `Enabled` | `true` | Master switch. |
| `OutputDirectory` | `"chunk-dumps"` | Relative paths resolve against the app's base directory (e.g. `bin/Debug/net8.0/chunk-dumps`). Use an absolute path to redirect. |
| `CleanOnStart` | `true` | Wipes the per-source folder at the start of each cycle. |
| `IncludeFullDocument` | `true` | Also write `_full.txt` for each document. |

Writer failures are caught and logged; diagnostics never break indexing.

---

## 12. Configuration reference (`appsettings.json`)

```jsonc
{
  "Indexing": {
    "Interval": "00:15:00",
    "StartupDelay": "00:00:05",
    "ForceFullReindex": false,
    "MaxDocumentsPerCycle": 1000,

    "Chunking":  { "TargetCharacters": 1200, "MinCharacters": 300,
                   "Overlap": 150, "MaxHeadingDepth": 4 },

    "Embedding": { "Provider": "llm-service", "Model": "embeddinggemma",
                   "Dimensions": 768 },

    "LlmService": { "BaseUrl": "http://localhost:5133",
                    "RequestTimeout": "00:02:00",
                    "MaxParallelism": 4, "MaxRetries": 3 },

    "Qdrant":    { "Host": "localhost", "Port": 6334,
                   "CollectionName": "documentation_chunks",
                   "VectorSize": 1536,        // see §8.3 — recreate at 768
                   "Distance": "Cosine" },

    "Postgres":  { "Enabled": true, "EnsureSchema": true,
                   "ConnectionString": "" },  // §10.5 — set in Development/secrets

    "WikiJs":    { "Enabled": true,
                   "BaseUrl": "https://wiki.example.local",
                   "GraphQlEndpoint": "/graphql" },

    "ChunkDump": { "Enabled": true,
                   "OutputDirectory": "chunk-dumps",
                   "CleanOnStart": true,
                   "IncludeFullDocument": true }
  }
}
```

All values can be overridden by environment variables using the standard
`Indexing__Section__Key` convention (e.g. `Indexing__WikiJs__AccessToken=…`,
`Indexing__Postgres__ConnectionString=…`). Secrets (Wiki.js access token,
Qdrant API key, PostgreSQL connection string) **must** come from
environment / user-secrets / `appsettings.Development.json`, never from
committed `appsettings.json`.

---

## 13. Running locally

1. **Qdrant** — `docker compose -f ../use-qdrant-db/docker-compose.yml up -d`.
   Verify with `curl http://localhost:6333/collections`.
2. **PostgreSQL** — `docker compose -f ../use-sql-db/docker-compose.yml up -d`.
   Connects on `localhost:5432`, db `use_metadata_db`. The worker creates the
   schema on first use when `Indexing:Postgres:EnsureSchema = true`.
3. **Use.LlmService.Api** — `dotnet run` in `../Use.LlmService.Api/`. It
   should listen on `http://localhost:5133` and connect to a local Ollama
   running `embeddinggemma`.
4. **Indexing worker** — `dotnet run` here. After one cycle, hit
   `http://localhost:6333/collections/documentation_chunks` and you should
   see a non-zero `points_count`; the same chunks land in
   `rag_document_chunks`.

Interactive commands while the worker is running (`ConsoleCommandListener`):

| Command | Effect |
|---|---|
| `index` | Run a cycle now across all connectors. |
| `index wikijs` | Reindex only Wiki.js. |
| `index wikijs 265` | Reindex a single page id. |
| `search-sql wikijs "Humanet August update"` | Test the PostgreSQL lexical/BM25 search. |

---

## 14. Numbers you actually care about

| Question | Today's answer |
|---|---|
| Embedding model | `embeddinggemma` (via Ollama, behind `Use.LlmService.Api`) |
| Vector dimensions per chunk | **768** floats |
| Distance metric | Cosine |
| Vectors per document | Variable; equals `DocumentChunk` count, typically a handful to a few dozen for normal Wiki.js pages |
| Target chunk size | **1200 characters** (soft); min **300**, overlap **150** |
| Concurrency to LLM API | up to **4** parallel embedding calls |
| What goes into the vector | enriched header (system/title/path/heading-path/chunk-order) + clean chunk text |
| What goes into RAG context | clean chunk text **only** (`payload.text` / `rag_document_chunks.text`) |
| Lexical store | PostgreSQL `rag_document_chunks` (full-text `tsvector`, shared `chunk_id`) |
| Re-indexing semantics | Full delete-then-upsert per document in **both** Qdrant and PostgreSQL; incremental discovery by `UpdatedAt` cursor unless `ForceFullReindex=true` |

---

## 15. TODO / known gaps

- HTML parser (AngleSharp) for non-Markdown sources.
- BM25 scoring tuning / language-specific text-search config (currently the
  `simple` dictionary; consider per-locale dictionaries or a custom BM25).
- Per-document permissions persisted alongside vectors/chunks and enforced at
  retrieval time (`permission_scope` / `access_metadata` columns are reserved).
- Map `Qdrant:VectorSize` automatically from `Embedding:Dimensions` to remove
  the manual mismatch foot-gun.
- Content-hash on `DocumentChunk.Text` to skip re-embedding unchanged chunks
  across cycles.
- Friendlier error mapping when the LLM API, Qdrant, or PostgreSQL is unreachable.

