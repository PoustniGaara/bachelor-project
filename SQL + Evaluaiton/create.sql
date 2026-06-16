-- Optional, but useful for UUID generation
CREATE EXTENSION IF NOT EXISTS pgcrypto;

-- ============================================================
-- 1. Source documents
--    One record represents one original page/document/file.
-- ============================================================

CREATE TABLE rag_source_documents (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),

    -- Example: 'WikiJs', 'SharePoint', 'AzureDevOps'
    source_system TEXT NOT NULL,

    -- ID from the original source system.
    -- Example for Wiki.js: '94'
    source_document_id TEXT NOT NULL,

    -- Human-readable document metadata
    title TEXT NOT NULL,
    description TEXT NULL,
    path TEXT NULL,
    source_url TEXT NULL,

    -- Source timestamps
    source_created_at TIMESTAMPTZ NULL,
    source_updated_at TIMESTAMPTZ NULL,

    -- Indexing timestamps
    indexed_at TIMESTAMPTZ NOT NULL DEFAULT now(),

    -- Optional raw metadata for source-specific values
    metadata JSONB NULL,

    CONSTRAINT uq_rag_source_documents_source_doc
        UNIQUE (source_system, source_document_id)
);


-- ============================================================
-- 2. Document chunks
--    One record represents one searchable chunk.
-- ============================================================

CREATE TABLE rag_document_chunks (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),

    -- Stable chunk ID shared with Qdrant.
    -- Example: 'WikiJs:94:2'
    chunk_id TEXT NOT NULL UNIQUE,

    source_document_ref_id UUID NOT NULL
        REFERENCES rag_source_documents(id)
        ON DELETE CASCADE,

    -- Duplicated for easier filtering and debugging.
    -- This also avoids joining source_documents in every retrieval query.
    source_system TEXT NOT NULL,
    source_document_id TEXT NOT NULL,

    -- Ordering and structure
    chunk_order INTEGER NOT NULL,
    heading_path TEXT NULL,
    heading_level INTEGER NULL,

    -- Example: ['Table'], ['Paragraph'], ['Heading', 'Paragraph']
    block_kinds TEXT[] NULL,

    -- Main text used for answer generation
    text TEXT NOT NULL,

    -- Text used for lexical search / BM25.
    -- This can include title, path, description, heading path, and content.
    search_text TEXT NOT NULL,

    -- Useful for access control later
    permission_scope TEXT NULL,
    access_metadata JSONB NULL,

    -- Source and indexing timestamps
    source_created_at TIMESTAMPTZ NULL,
    source_updated_at TIMESTAMPTZ NULL,
    indexed_at TIMESTAMPTZ NOT NULL DEFAULT now(),

    -- Source-specific metadata that does not deserve its own column
    metadata JSONB NULL,

    -- PostgreSQL lexical search column.
    -- 'simple' is safer for Slovak/Czech/internal product names than 'english'.
    search_vector TSVECTOR GENERATED ALWAYS AS (
        to_tsvector('simple', search_text)
    ) STORED,

    CONSTRAINT uq_rag_document_chunks_doc_order
        UNIQUE (source_system, source_document_id, chunk_order)
);


-- ============================================================
-- 3. Indexes for filtering and lexical retrieval
-- ============================================================

-- Fast source filtering, e.g. only WikiJs chunks
CREATE INDEX ix_rag_chunks_source_system
    ON rag_document_chunks (source_system);

-- Fast lookup of all chunks belonging to one source document/page
CREATE INDEX ix_rag_chunks_source_document
    ON rag_document_chunks (source_system, source_document_id);

-- Fast ordering of neighboring chunks
CREATE INDEX ix_rag_chunks_document_order
    ON rag_document_chunks (source_document_ref_id, chunk_order);

-- Full-text lexical index
CREATE INDEX ix_rag_chunks_search_vector
    ON rag_document_chunks
    USING GIN (search_vector);

-- Optional metadata index
CREATE INDEX ix_rag_chunks_metadata
    ON rag_document_chunks
    USING GIN (metadata);

CREATE INDEX ix_rag_documents_metadata
    ON rag_source_documents
    USING GIN (metadata);


   -- Optional, but useful for UUID generation
CREATE EXTENSION IF NOT EXISTS pgcrypto;

-- ============================================================
-- 1. Application users
--    One record represents one authenticated Microsoft Entra user.
--    Passwords are not stored here.
-- ============================================================

CREATE TABLE app_user (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),

    -- Microsoft Entra ID object identifier of the user.
    -- This is more stable than email.
    entra_object_id TEXT NOT NULL,

    -- Microsoft Entra tenant identifier.
    tenant_id TEXT NOT NULL,

    -- User email / preferred username from Microsoft token.
    email TEXT NOT NULL,

    -- Human-readable name from Microsoft token.
    display_name TEXT NULL,

    -- Allows disabling user access locally without deleting history.
    is_active BOOLEAN NOT NULL DEFAULT TRUE,

    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    last_login_at TIMESTAMPTZ NULL,

    CONSTRAINT uq_app_user_entra
        UNIQUE (tenant_id, entra_object_id)
);


-- ============================================================
-- 2. Chat sessions
--    One record represents one conversation thread.
-- ============================================================

CREATE TABLE chat_session (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),

    user_id UUID NOT NULL
        REFERENCES app_user(id)
        ON DELETE CASCADE,

    title TEXT NULL,

    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at TIMESTAMPTZ NULL
);


-- ============================================================
-- 3. Chat messages
--    One record represents one message in a conversation.
-- ============================================================

CREATE TABLE chat_message (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),

    chat_session_id UUID NOT NULL
        REFERENCES chat_session(id)
        ON DELETE CASCADE,

    -- Expected values: 'user', 'assistant', 'system'
    role TEXT NOT NULL,

    content TEXT NOT NULL,

    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),

    CONSTRAINT chk_chat_message_role
        CHECK (role IN ('user', 'assistant', 'system'))
);


-- ============================================================
-- 4. RAG query log
--    One record represents one RAG execution:
--    user question -> retrieval -> generation -> assistant answer.
-- ============================================================

CREATE TABLE rag_query_log (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),

    -- Message where this RAG query happened.
    chat_message_id UUID NOT NULL
        REFERENCES chat_message(id)
        ON DELETE CASCADE,

    -- Original user query text.
    original_query TEXT NOT NULL,

    -- Query after rewriting, HYDE, expansion, normalization, etc.
    augmented_query TEXT NULL,

    -- Example values:
    -- 'semantic', 'lexical', 'hybrid_rrf', 'semantic_with_hyde'
    retrieval_strategy TEXT NOT NULL,

    started_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    completed_at TIMESTAMPTZ NULL,

    -- Total duration of whole RAG pipeline.
    duration_ms INTEGER NULL,

    -- Retrieval-only duration.
    retrieval_duration_ms INTEGER NULL,

    -- LLM generation-only duration.
    generation_duration_ms INTEGER NULL,

    -- How many chunks were retrieved before final filtering/selection.
    total_retrieved_chunks INTEGER NULL,

    -- How many chunks were actually included in the final context.
    selected_context_chunks INTEGER NULL,

    -- Example values:
    -- 'completed', 'failed', 'cancelled', 'no_relevant_context'
    answer_status TEXT NOT NULL DEFAULT 'completed',

    error_message TEXT NULL,

    -- Suggested values:
    -- -1 = bad, 1 = good, NULL = not rated
    user_rating SMALLINT NULL,

    user_feedback TEXT NULL,

    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),

    CONSTRAINT chk_rag_query_log_user_rating
        CHECK (user_rating IS NULL OR user_rating IN (-1, 1)),

    CONSTRAINT chk_rag_query_log_answer_status
        CHECK (answer_status IN ('completed', 'failed', 'cancelled', 'no_relevant_context')),

    CONSTRAINT chk_rag_query_log_duration_ms
        CHECK (duration_ms IS NULL OR duration_ms >= 0),

    CONSTRAINT chk_rag_query_log_retrieval_duration_ms
        CHECK (retrieval_duration_ms IS NULL OR retrieval_duration_ms >= 0),

    CONSTRAINT chk_rag_query_log_generation_duration_ms
        CHECK (generation_duration_ms IS NULL OR generation_duration_ms >= 0),

    CONSTRAINT chk_rag_query_log_total_retrieved_chunks
        CHECK (total_retrieved_chunks IS NULL OR total_retrieved_chunks >= 0),

    CONSTRAINT chk_rag_query_log_selected_context_chunks
        CHECK (selected_context_chunks IS NULL OR selected_context_chunks >= 0)
);


-- ============================================================
-- 5. Retrieved chunk log
--    One record represents one retrieved chunk for one RAG query.
--    For example, top 30 retrieved chunks = 30 rows.
-- ============================================================

CREATE TABLE rag_retrieved_chunk_log (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),

    rag_query_log_id UUID NOT NULL
        REFERENCES rag_query_log(id)
        ON DELETE CASCADE,

    -- Rank after final fusion/ranking.
    -- 1 = best result.
    rank INTEGER NOT NULL,

    -- Referenced source document.
    document_id UUID NOT NULL
        REFERENCES rag_source_documents(id)
        ON DELETE CASCADE,

    -- Referenced chunk.
    -- This references the internal UUID primary key of rag_document_chunks.
    chunk_id UUID NOT NULL
        REFERENCES rag_document_chunks(id)
        ON DELETE CASCADE,

    -- Score from semantic/vector retrieval.
    semantic_score DOUBLE PRECISION NULL,

    -- Score from lexical/BM25 retrieval.
    lexical_score DOUBLE PRECISION NULL,

    -- Reciprocal Rank Fusion score or final fusion score.
    rrf_score DOUBLE PRECISION NULL,

    -- True if this chunk was included in the final prompt context.
    was_selected_for_context BOOLEAN NOT NULL DEFAULT FALSE,

    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),

    CONSTRAINT uq_rag_retrieved_chunk_log_query_rank
        UNIQUE (rag_query_log_id, rank),

    CONSTRAINT uq_rag_retrieved_chunk_log_query_chunk
        UNIQUE (rag_query_log_id, chunk_id),

    CONSTRAINT chk_rag_retrieved_chunk_log_rank
        CHECK (rank > 0)
);


-- ============================================================
-- 6. Indexes
-- ============================================================

-- Fast user lookup after Microsoft authentication
CREATE INDEX ix_app_user_email
    ON app_user (email);

CREATE INDEX ix_app_user_entra_object_id
    ON app_user (entra_object_id);


-- Fast loading of user's chat sessions
CREATE INDEX ix_chat_session_user_id
    ON chat_session (user_id);

CREATE INDEX ix_chat_session_user_created
    ON chat_session (user_id, created_at DESC);


-- Fast loading of messages in a session
CREATE INDEX ix_chat_message_session_created
    ON chat_message (chat_session_id, created_at ASC);

CREATE INDEX ix_chat_message_role
    ON chat_message (role);


-- Fast lookup of RAG logs by user/session/message

CREATE INDEX ix_rag_query_log_chat_message_id
    ON rag_query_log (chat_message_id);

CREATE INDEX ix_rag_query_log_created_at
    ON rag_query_log (created_at DESC);

CREATE INDEX ix_rag_query_log_answer_status
    ON rag_query_log (answer_status);

CREATE INDEX ix_rag_query_log_user_rating
    ON rag_query_log (user_rating);


-- Fast loading of retrieved chunks for one RAG query
CREATE INDEX ix_rag_retrieved_chunk_log_query_rank
    ON rag_retrieved_chunk_log (rag_query_log_id, rank);

CREATE INDEX ix_rag_retrieved_chunk_log_document_id
    ON rag_retrieved_chunk_log (document_id);

CREATE INDEX ix_rag_retrieved_chunk_log_chunk_id
    ON rag_retrieved_chunk_log (chunk_id);

CREATE INDEX ix_rag_retrieved_chunk_log_created_at
    ON rag_retrieved_chunk_log (created_at DESC); 