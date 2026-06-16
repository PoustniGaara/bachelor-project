import { useCallback, useEffect, useRef, useState } from 'react'
import ReactMarkdown from 'react-markdown'
import ChatInputPanel from './ChatInputPanel'
import { useAuth } from '../auth/useAuth'
import { askChat, getChatMessages, getChatSessions, rateRagQueryLog } from '../services/chatApi'
import { ApiError } from '../services/apiClient'
import type { ChatSession, RatingValue, SourceReference, UiCitation } from '../types/chat'

type UiMessage = {
  /** Local UI id (stable for React keys). */
  id: string
  /** Backend message id, when persisted. */
  backendId?: string
  role: 'user' | 'assistant' | 'system'
  content: string
  citations?: UiCitation[]
  durationMs?: number
  createdAt?: string
  /** Present on freshly generated assistant answers — enables rating. */
  ragQueryLogId?: string
  rating?: RatingValue
  failed?: boolean
}

function genId(): string {
  if (typeof crypto !== 'undefined' && 'randomUUID' in crypto) return crypto.randomUUID()
  return `${Date.now()}-${Math.random().toString(16).slice(2)}`
}

function mapSourceToCitation(source: SourceReference): UiCitation {
  const system = source.sourceSystem?.trim()
  const title = source.title?.trim()
  const label =
    system && title
      ? `${system} / ${title}`
      : title ?? system ?? source.url ?? source.chunkId ?? 'Zdroj'
  return { label, url: source.url ?? undefined }
}

function formatDuration(durationMs: number): string {
  const seconds = (durationMs / 1000).toFixed(1).replace('.', ',')
  return `Vygenerované za ${seconds} s`
}

function SidebarIcon({ size = 20 }: { size?: number }) {
  return (
    <svg
      width={size}
      height={size}
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth="2"
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden="true"
    >
      <rect x="3" y="3" width="18" height="18" rx="2" />
      <line x1="9" y1="3" x2="9" y2="21" />
    </svg>
  )
}

function NewChatIcon({ size = 20 }: { size?: number }) {
  return (
    <svg
      width={size}
      height={size}
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth="2"
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden="true"
    >
      <path d="M12 20h9" />
      <path d="M16.5 3.5a2.121 2.121 0 0 1 3 3L7 19l-4 1 1-4 12.5-12.5z" />
    </svg>
  )
}

function LogoutIcon({ size = 18 }: { size?: number }) {
  return (
    <svg
      width={size}
      height={size}
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth="2"
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden="true"
    >
      <path d="M9 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h4" />
      <polyline points="16 17 21 12 16 7" />
      <line x1="21" y1="12" x2="9" y2="12" />
    </svg>
  )
}

function ThumbIcon({ direction, size = 16 }: { direction: 'up' | 'down'; size?: number }) {
  const transform = direction === 'down' ? 'rotate(180 12 12)' : undefined
  return (
    <svg
      width={size}
      height={size}
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth="2"
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden="true"
    >
      <g transform={transform}>
        <path d="M7 10v12" />
        <path d="M15 5.88 14 10h5.83a2 2 0 0 1 1.92 2.56l-2.33 8A2 2 0 0 1 17.5 22H4a2 2 0 0 1-2-2v-8a2 2 0 0 1 2-2h2.76a2 2 0 0 0 1.79-1.11L12 2a3.13 3.13 0 0 1 3 3.88Z" />
      </g>
    </svg>
  )
}

export default function ChatLayout() {
  const { user, logout } = useAuth()

  const [query, setQuery] = useState('')
  const [isLoading, setIsLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [sidebarOpen, setSidebarOpen] = useState(true)
  const [logoHovered, setLogoHovered] = useState(false)

  const [messages, setMessages] = useState<UiMessage[]>([])
  const [sessions, setSessions] = useState<ChatSession[]>([])
  const [activeSessionId, setActiveSessionId] = useState<string | null>(null)
  const [sessionsLoading, setSessionsLoading] = useState(false)
  const [sessionsError, setSessionsError] = useState<string | null>(null)
  const [messagesLoading, setMessagesLoading] = useState(false)

  const abortRef = useRef<AbortController | null>(null)
  const sessionsAbortRef = useRef<AbortController | null>(null)
  const messagesAbortRef = useRef<AbortController | null>(null)

  const displayName = user?.name ?? '—'
  const displayEmail = user?.email ?? ''
  const initials = user?.initials ?? '?'

  const loadSessions = useCallback(async () => {
    sessionsAbortRef.current?.abort()
    const controller = new AbortController()
    sessionsAbortRef.current = controller

    setSessionsLoading(true)
    setSessionsError(null)
    try {
      const result = await getChatSessions(controller.signal)
      setSessions(result)
    } catch (err) {
      if ((err as Error).name === 'AbortError') return
      const apiErr = err as ApiError
      setSessionsError(
        apiErr.status === 503
          ? 'História nie je dostupná (databáza je vypnutá).'
          : 'Konverzácie sa nepodarilo načítať.',
      )
    } finally {
      if (sessionsAbortRef.current === controller) setSessionsLoading(false)
    }
  }, [])

  useEffect(() => {
    void loadSessions()
    return () => {
      abortRef.current?.abort()
      sessionsAbortRef.current?.abort()
      messagesAbortRef.current?.abort()
    }
  }, [loadSessions])

  const loadMessages = async (sessionId: string) => {
    messagesAbortRef.current?.abort()
    const controller = new AbortController()
    messagesAbortRef.current = controller

    setMessagesLoading(true)
    setError(null)
    try {
      const result = await getChatMessages(sessionId, controller.signal)
      setMessages(
        result.map((m) => ({
          id: genId(),
          backendId: m.id,
          role: m.role,
          content: m.content,
          createdAt: m.createdAt,
        })),
      )
    } catch (err) {
      if ((err as Error).name === 'AbortError') return
      setError((err as Error).message ?? 'Správy sa nepodarilo načítať.')
    } finally {
      if (messagesAbortRef.current === controller) setMessagesLoading(false)
    }
  }

  const handleSelectSession = (sessionId: string) => {
    if (sessionId === activeSessionId) return
    abortRef.current?.abort()
    setIsLoading(false)
    setActiveSessionId(sessionId)
    void loadMessages(sessionId)
  }

  const handleNewChat = () => {
    abortRef.current?.abort()
    messagesAbortRef.current?.abort()
    setActiveSessionId(null)
    setMessages([])
    setQuery('')
    setError(null)
    setIsLoading(false)
    setMessagesLoading(false)
  }

  const handleSend = async () => {
    const question = query.trim()
    if (!question || isLoading) return

    const userLocalId = genId()
    const userMessage: UiMessage = { id: userLocalId, role: 'user', content: question }

    setMessages((prev) => [...prev, userMessage])
    setQuery('')
    setError(null)
    setIsLoading(true)

    abortRef.current?.abort()
    const controller = new AbortController()
    abortRef.current = controller

    const startTime = performance.now()

    try {
      const response = await askChat(
        { question, chatSessionId: activeSessionId },
        controller.signal,
      )
      const durationMs = performance.now() - startTime

      const assistantMessage: UiMessage = {
        id: genId(),
        backendId: response.assistantMessageId ?? undefined,
        role: 'assistant',
        content: response.answer,
        citations: response.sources?.map(mapSourceToCitation),
        durationMs,
        ragQueryLogId: response.ragQueryLogId ?? undefined,
        rating: null,
      }

      setMessages((prev) => {
        const patched = response.userMessageId
          ? prev.map((m) =>
              m.id === userLocalId ? { ...m, backendId: response.userMessageId ?? undefined } : m,
            )
          : prev
        return [...patched, assistantMessage]
      })

      // Adopt the (possibly newly created) session and refresh the sidebar.
      if (response.chatSessionId) setActiveSessionId(response.chatSessionId)
      void loadSessions()
    } catch (err) {
      if ((err as Error).name === 'AbortError') return
      setMessages((prev) => prev.map((m) => (m.id === userLocalId ? { ...m, failed: true } : m)))
      setError((err as Error).message ?? 'Niečo sa pokazilo.')
    } finally {
      if (abortRef.current === controller) setIsLoading(false)
    }
  }

  const handleRate = async (message: UiMessage, rating: RatingValue) => {
    if (!message.ragQueryLogId) return
    const nextRating: RatingValue = message.rating === rating ? null : rating
    const previous = message.rating ?? null

    setMessages((prev) => prev.map((m) => (m.id === message.id ? { ...m, rating: nextRating } : m)))
    try {
      await rateRagQueryLog(message.ragQueryLogId, nextRating)
    } catch {
      setMessages((prev) =>
        prev.map((m) => (m.id === message.id ? { ...m, rating: previous } : m)),
      )
      setError('Hodnotenie sa nepodarilo uložiť.')
    }
  }

  const handleKeyDown = (e: React.KeyboardEvent<HTMLInputElement>) => {
    if (e.key === 'Enter' && !e.shiftKey) {
      e.preventDefault()
      void handleSend()
    }
  }

  const showEmptyState = messages.length === 0 && !messagesLoading

  const navItemBaseStyle: React.CSSProperties = {
    padding: '0.5rem 0.75rem',
    borderRadius: '8px',
    cursor: 'pointer',
    color: '#ececec',
    fontSize: '0.9rem',
    marginBottom: '0.25rem',
    overflow: 'hidden',
    textOverflow: 'ellipsis',
  }

  const iconButtonStyle: React.CSSProperties = {
    background: 'transparent',
    border: 'none',
    color: '#ececec',
    fontSize: '1.25rem',
    cursor: 'pointer',
    lineHeight: 1,
  }

  const errorBoxStyle: React.CSSProperties = {
    marginTop: '1rem',
    padding: '1rem',
    border: '1px solid #5c2b2b',
    background: '#3a1f1f',
    color: '#f5b5b5',
    borderRadius: '12px',
  }

  return (
    <div
      style={{
        display: 'flex',
        height: '100vh',
        background: '#212121',
        color: '#ececec',
        fontFamily: 'system-ui, sans-serif',
      }}
    >
      {/* Collapsible sidebar with chat history */}
      <aside
        style={{
          width: sidebarOpen ? 260 : 72,
          transition: 'width 0.2s ease',
          overflow: 'hidden',
          background: '#171717',
          whiteSpace: 'nowrap',
          display: 'flex',
          flexDirection: 'column',
        }}
      >
        {/* Logo area + sidebar toggle */}
        <div
          onMouseEnter={() => setLogoHovered(true)}
          onMouseLeave={() => setLogoHovered(false)}
          style={{
            display: 'flex',
            alignItems: 'center',
            justifyContent: sidebarOpen ? 'space-between' : 'center',
            padding: sidebarOpen ? '1.25rem 1rem' : '1.25rem 0',
            minHeight: 72,
          }}
        >
          {sidebarOpen ? (
            <>
              <img
                src="USE_logo_lockup_transparent.png"
                alt="Logo"
                style={{ height: 40, width: 'auto', objectFit: 'contain' }}
              />
              <button
                onClick={() => setSidebarOpen(false)}
                aria-label="Zbaliť panel"
                title="Zbaliť panel"
                style={iconButtonStyle}
              >
                <SidebarIcon />
              </button>
            </>
          ) : (
            <button
              onClick={() => setSidebarOpen(true)}
              aria-label="Rozbaliť panel"
              title="Rozbaliť panel"
              style={{
                ...iconButtonStyle,
                display: 'flex',
                alignItems: 'center',
                justifyContent: 'center',
                padding: 0,
                height: 32,
                width: 32,
              }}
            >
              {logoHovered ? (
                <SidebarIcon size={28} />
              ) : (
                <img
                  src="USE_logo_symbol_transparent.png"
                  alt="Logo"
                  style={{ height: 32, width: 'auto', objectFit: 'contain' }}
                />
              )}
            </button>
          )}
        </div>

        {/* New chat button */}
        <div style={{ padding: sidebarOpen ? '0 1rem 0.5rem' : '0 0 0.5rem', display: 'flex', justifyContent: 'center' }}>
          <button
            onClick={handleNewChat}
            aria-label="Nový chat"
            title="Nový chat"
            style={{
              display: 'flex',
              alignItems: 'center',
              gap: sidebarOpen ? '0.6rem' : 0,
              justifyContent: sidebarOpen ? 'flex-start' : 'center',
              width: sidebarOpen ? '100%' : 40,
              height: 40,
              padding: sidebarOpen ? '0 0.75rem' : 0,
              background: 'transparent',
              border: '1px solid #2f2f2f',
              borderRadius: '10px',
              color: '#ececec',
              cursor: 'pointer',
              fontSize: '0.9rem',
              fontWeight: 500,
            }}
          >
            <NewChatIcon size={18} />
            {sidebarOpen && <span>Nový chat</span>}
          </button>
        </div>

        {/* Conversation list only when open — backed by GET /api/chat/sessions */}
        {sidebarOpen && (
          <div style={{ padding: '1rem', flex: 1, overflowY: 'auto' }}>
            <h3
              style={{
                color: '#9b9b9b',
                fontSize: '0.75rem',
                textTransform: 'uppercase',
                letterSpacing: '0.05em',
                marginBottom: '0.75rem',
              }}
            >
              Konverzácie
            </h3>

            {sessionsLoading && (
              <div style={{ color: '#7d7d7d', fontSize: '0.85rem', padding: '0.25rem 0.75rem' }}>
                Načítavam…
              </div>
            )}

            {!sessionsLoading && sessionsError && (
              <div style={{ color: '#7d7d7d', fontSize: '0.8rem', padding: '0.25rem 0.75rem' }}>
                {sessionsError}
              </div>
            )}

            {!sessionsLoading && !sessionsError && sessions.length === 0 && (
              <div style={{ color: '#7d7d7d', fontSize: '0.85rem', padding: '0.25rem 0.75rem' }}>
                Žiadne konverzácie
              </div>
            )}

            {!sessionsLoading &&
              sessions.map((session) => {
                const selected = session.id === activeSessionId
                return (
                  <div
                    key={session.id}
                    onClick={() => handleSelectSession(session.id)}
                    title={session.title ?? 'Nová konverzácia'}
                    style={{
                      ...navItemBaseStyle,
                      background: selected ? '#2f2f2f' : 'transparent',
                    }}
                  >
                    {session.title?.trim() || 'Nová konverzácia'}
                  </div>
                )
              })}
          </div>
        )}

        {/* Profile / logout — real user from auth (MSAL account or dev identity) */}
        <div
          style={{
            marginTop: sidebarOpen ? 0 : 'auto',
            padding: sidebarOpen ? '0.75rem' : '0.75rem 0',
            borderTop: '1px solid #2f2f2f',
          }}
        >
          <div
            style={{
              display: 'flex',
              alignItems: 'center',
              gap: '0.6rem',
              justifyContent: sidebarOpen ? 'space-between' : 'center',
            }}
          >
            <div style={{ display: 'flex', alignItems: 'center', gap: '0.6rem', minWidth: 0 }}>
              <span
                style={{
                  display: 'flex',
                  alignItems: 'center',
                  justifyContent: 'center',
                  width: 32,
                  height: 32,
                  flexShrink: 0,
                  borderRadius: '50%',
                  background: '#6c5ce7',
                  color: '#fff',
                  fontSize: '0.8rem',
                  fontWeight: 600,
                }}
              >
                {initials}
              </span>
              {sidebarOpen && (
                <div style={{ minWidth: 0 }}>
                  <div
                    style={{
                      fontSize: '0.9rem',
                      fontWeight: 500,
                      overflow: 'hidden',
                      textOverflow: 'ellipsis',
                    }}
                  >
                    {displayName}
                  </div>
                  {displayEmail && (
                    <div
                      style={{
                        fontSize: '0.72rem',
                        color: '#7d7d7d',
                        overflow: 'hidden',
                        textOverflow: 'ellipsis',
                      }}
                    >
                      {displayEmail}
                    </div>
                  )}
                </div>
              )}
            </div>

            {sidebarOpen && (
              <button
                onClick={() => void logout()}
                aria-label="Odhlásiť sa"
                title="Odhlásiť sa"
                style={{ ...iconButtonStyle, display: 'flex', alignItems: 'center' }}
              >
                <LogoutIcon />
              </button>
            )}
          </div>
        </div>
      </aside>

      {/* Main column */}
      <main
        style={{
          flex: 1,
          display: 'flex',
          flexDirection: 'column',
          minWidth: 0,
          position: 'relative',
        }}
      >
        {showEmptyState ? (
          /* ---------- EMPTY STATE (centered, subtle glow) ---------- */
          <div
            style={{
              flex: 1,
              display: 'flex',
              flexDirection: 'column',
              alignItems: 'center',
              justifyContent: 'center',
              padding: '1rem',
              position: 'relative',
              overflow: 'hidden',
            }}
          >
            {/* USE-inspired glow: orange/yellow + purple/magenta, kept subtle */}
            <div
              aria-hidden="true"
              style={{
                position: 'absolute',
                width: 720,
                height: 720,
                maxWidth: '90%',
                borderRadius: '50%',
                filter: 'blur(120px)',
                opacity: 0.18,
                pointerEvents: 'none',
                background:
                  'radial-gradient(circle at 30% 35%, rgba(255,170,60,0.9), transparent 60%), radial-gradient(circle at 70% 65%, rgba(170,60,220,0.9), transparent 60%)',
              }}
            />

            <div
              style={{
                position: 'relative',
                width: '100%',
                maxWidth: 720,
                textAlign: 'center',
              }}
            >
              <h1
                style={{
                  fontSize: '1.75rem',
                  fontWeight: 600,
                  margin: '0 0 2.5rem',
                  color: '#ececec',
                }}
              >
                Ako vám môžem pomôcť?
              </h1>

              <ChatInputPanel
                query={query}
                setQuery={setQuery}
                onSend={() => void handleSend()}
                onKeyDown={handleKeyDown}
                isLoading={isLoading}
              />

              {error && <div style={errorBoxStyle}>{error}</div>}
            </div>
          </div>
        ) : (
          /* ---------- CHAT MODE (messages + bottom input) ---------- */
          <>
            <section style={{ flex: 1, overflowY: 'auto', width: '100%' }}>
              <div style={{ maxWidth: 768, margin: '0 auto', padding: '1rem' }}>
                {messagesLoading && (
                  <div style={{ padding: '1rem', color: '#9b9b9b' }}>Načítavam správy…</div>
                )}

                {messages.map((message) => {
                  const isUser = message.role === 'user'
                  return (
                    <div
                      key={message.id}
                      style={{
                        display: 'flex',
                        flexDirection: 'column',
                        alignItems: isUser ? 'flex-end' : 'flex-start',
                        marginBottom: '1.5rem',
                      }}
                    >
                      <div
                        style={{
                          maxWidth: isUser ? '80%' : '100%',
                          padding: isUser ? '0.75rem 1rem' : 0,
                          borderRadius: '16px',
                          background: isUser ? '#2f2f2f' : 'transparent',
                          border: message.failed ? '1px solid #5c2b2b' : undefined,
                        }}
                      >
                        {isUser ? (
                          <p style={{ whiteSpace: 'pre-wrap', margin: 0 }}>{message.content}</p>
                        ) : (
                          <ReactMarkdown>{message.content}</ReactMarkdown>
                        )}

                        {message.citations && message.citations.length > 0 && (
                          <div style={{ marginTop: '0.75rem', fontSize: '0.85rem', color: '#9b9b9b' }}>
                            <strong>Citácie:</strong>
                            <ul style={{ margin: '0.25rem 0 0', paddingLeft: '1.25rem' }}>
                              {message.citations.map((citation, index) => (
                                <li key={index}>
                                  {citation.url ? (
                                    <a
                                      href={citation.url}
                                      target="_blank"
                                      rel="noreferrer"
                                      style={{ color: '#9b9b9b' }}
                                    >
                                      {citation.label}
                                    </a>
                                  ) : (
                                    citation.label
                                  )}
                                </li>
                              ))}
                            </ul>
                          </div>
                        )}
                      </div>

                      {message.failed && (
                        <div style={{ marginTop: '0.4rem', fontSize: '0.78rem', color: '#f5b5b5' }}>
                          Správu sa nepodarilo odoslať.
                        </div>
                      )}

                      {!isUser && message.durationMs != null && (
                        <div style={{ marginTop: '0.4rem', fontSize: '0.78rem', color: '#7d7d7d' }}>
                          {formatDuration(message.durationMs)}
                        </div>
                      )}

                      {!isUser && message.ragQueryLogId && (
                        <div style={{ marginTop: '0.4rem', display: 'flex', gap: '0.35rem' }}>
                          <button
                            onClick={() => void handleRate(message, 1)}
                            aria-label="Dobrá odpoveď"
                            title="Dobrá odpoveď"
                            style={{
                              ...iconButtonStyle,
                              display: 'flex',
                              alignItems: 'center',
                              color: message.rating === 1 ? '#7dd87d' : '#7d7d7d',
                            }}
                          >
                            <ThumbIcon direction="up" />
                          </button>
                          <button
                            onClick={() => void handleRate(message, -1)}
                            aria-label="Zlá odpoveď"
                            title="Zlá odpoveď"
                            style={{
                              ...iconButtonStyle,
                              display: 'flex',
                              alignItems: 'center',
                              color: message.rating === -1 ? '#f5b5b5' : '#7d7d7d',
                            }}
                          >
                            <ThumbIcon direction="down" />
                          </button>
                        </div>
                      )}
                    </div>
                  )
                })}

                {isLoading && (
                  <div style={{ padding: '1rem', color: '#9b9b9b' }}>Asistent premýšľa…</div>
                )}

                {error && <div style={errorBoxStyle}>{error}</div>}
              </div>
            </section>

            <footer
              style={{
                padding: '1rem 1rem 1.5rem',
                width: '100%',
                maxWidth: 768,
                margin: '0 auto',
              }}
            >
              <ChatInputPanel
                query={query}
                setQuery={setQuery}
                onSend={() => void handleSend()}
                onKeyDown={handleKeyDown}
                isLoading={isLoading}
              />
            </footer>
          </>
        )}
      </main>
    </div>
  )
}