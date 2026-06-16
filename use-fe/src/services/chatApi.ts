import type {
  ChatMessage,
  ChatRequest,
  ChatResponse,
  ChatSession,
  RatingValue,
} from '../types/chat'
import { apiRequest } from './apiClient'

const BASE = '/api/chat'

/** POST /api/chat — run the RAG pipeline; creates/continues a session. */
export function askChat(request: ChatRequest, signal?: AbortSignal): Promise<ChatResponse> {
  return apiRequest<ChatResponse>(BASE, { method: 'POST', body: request, signal })
}

/** GET /api/chat/sessions — current user's chat sessions (newest first). */
export function getChatSessions(signal?: AbortSignal): Promise<ChatSession[]> {
  return apiRequest<ChatSession[]>(`${BASE}/sessions`, { signal })
}

/** POST /api/chat/sessions — create a new empty chat session. */
export function createChatSession(title?: string | null, signal?: AbortSignal): Promise<ChatSession> {
  return apiRequest<ChatSession>(`${BASE}/sessions`, { method: 'POST', body: { title }, signal })
}

/** GET /api/chat/sessions/{id}/messages — messages of an owned session. */
export function getChatMessages(chatSessionId: string, signal?: AbortSignal): Promise<ChatMessage[]> {
  return apiRequest<ChatMessage[]>(
    `${BASE}/sessions/${encodeURIComponent(chatSessionId)}/messages`,
    { signal },
  )
}

/** POST /api/chat/rag-query-logs/{id}/rating — store rating/feedback for an answer. */
export function rateRagQueryLog(
  ragQueryLogId: string,
  rating: RatingValue,
  feedback?: string | null,
  signal?: AbortSignal,
): Promise<void> {
  return apiRequest<void>(
    `${BASE}/rag-query-logs/${encodeURIComponent(ragQueryLogId)}/rating`,
    { method: 'POST', body: { rating, feedback }, signal },
  )
}

// NOTE: The backend has no `/api/users/me` endpoint, so there is no
// `getCurrentUser()` here — profile data comes from the auth account / dev
// identity (see `src/auth`).

