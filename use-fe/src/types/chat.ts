// ---------------------------------------------------------------------------
// Backend contract (Use.Application.Service). JSON is camelCase.
// ---------------------------------------------------------------------------

/** Message roles as stored in the backend `chat_message` table. */
export type ChatRole = 'user' | 'assistant' | 'system'

/** Body of `POST /api/chat`. `chatSessionId` continues an existing session. */
export type ChatRequest = {
  question: string
  /** null/undefined → backend creates a new session and returns its id. */
  chatSessionId?: string | null
}

/** One source/citation backing an answer (`ChatResponse.sources`). */
export type SourceReference = {
  title?: string | null
  url?: string | null
  sourceSystem?: string | null
  chunkId?: string | null
  score?: number
}

/** Raw retrieved chunk — not rendered in the UI today; typed loosely. */
export type RetrievedChunk = Record<string, unknown>

/** Body of `POST /api/chat` response. */
export type ChatResponse = {
  answer: string
  sources?: SourceReference[]
  retrievedChunks?: RetrievedChunk[]
  // Chat metadata added by the SQL-backed history feature (nullable):
  chatSessionId?: string | null
  userMessageId?: string | null
  assistantMessageId?: string | null
  ragQueryLogId?: string | null
}

/** One chat session (`GET /api/chat/sessions`). */
export type ChatSession = {
  id: string
  title?: string | null
  createdAt: string
  updatedAt?: string | null
}

/** One persisted message (`GET /api/chat/sessions/{id}/messages`). */
export type ChatMessage = {
  id: string
  role: ChatRole
  content: string
  createdAt: string
}

/** Body of `POST /api/chat/sessions`. */
export type CreateChatSessionRequest = {
  title?: string | null
}

/** Rating value: 1 = good, -1 = bad, null = clear. */
export type RatingValue = 1 | -1 | null

/** Body of the rating endpoints. */
export type RateRagAnswerRequest = {
  rating: RatingValue
  feedback?: string | null
}

/** Standard backend error body. */
export type ChatErrorBody = {
  error?: string
}

// ---------------------------------------------------------------------------
// UI-only view models
// ---------------------------------------------------------------------------

/** A citation as rendered in the UI (mapped from `SourceReference`). */
export type UiCitation = {
  label: string
  url?: string
}

/** The current user shown in the sidebar (from auth account / dev identity). */
export type CurrentUser = {
  name: string
  email: string
  initials: string
}
