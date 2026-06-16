type ChatInputPanelProps = {
    query: string
    setQuery: (value: string) => void
    onSend: () => void
    onKeyDown: (e: React.KeyboardEvent<HTMLInputElement>) => void
    isLoading: boolean
  }
  
  function SendIcon({ size = 18 }: { size?: number }) {
    return (
      <svg
        width={size}
        height={size}
        viewBox="0 0 24 24"
        fill="none"
        stroke="currentColor"
        strokeWidth="2.2"
        strokeLinecap="round"
        strokeLinejoin="round"
        aria-hidden="true"
      >
        <line x1="12" y1="19" x2="12" y2="5" />
        <polyline points="5 12 12 5 19 12" />
      </svg>
    )
  }
  
  export default function ChatInputPanel({
    query,
    setQuery,
    onSend,
    onKeyDown,
    isLoading,
  }: ChatInputPanelProps) {
    const canSend = !isLoading && query.trim().length > 0
  
    return (
      <div style={{ width: '100%' }}>
  
        {/* Input + round send button */}
        <div
          style={{
            display: 'flex',
            alignItems: 'center',
            gap: '0.5rem',
            background: '#2f2f2f',
            borderRadius: '16px',
            padding: '0.5rem',
            boxShadow: '0 8px 30px rgba(0,0,0,0.25)',
          }}
        >
          <input
            value={query}
            onChange={(e) => setQuery(e.target.value)}
            onKeyDown={onKeyDown}
            placeholder="Opýtajte sa na internú dokumentáciu..."
            disabled={isLoading}
            style={{
              flex: 1,
              padding: '0.5rem 0.75rem',
              background: 'transparent',
              border: 'none',
              color: '#ececec',
              outline: 'none',
              fontSize: '1rem',
            }}
          />
          <button
            onClick={onSend}
            disabled={!canSend}
            aria-label="Odoslať"
            title="Odoslať"
            style={{
              display: 'flex',
              alignItems: 'center',
              justifyContent: 'center',
              width: 36,
              height: 36,
              flexShrink: 0,
              padding: 0,
              borderRadius: '50%',
              border: 'none',
              background: canSend ? '#ececec' : '#4a4a4a',
              color: canSend ? '#171717' : '#8a8a8a',
              cursor: canSend ? 'pointer' : 'not-allowed',
              transition: 'background 0.15s ease, color 0.15s ease',
            }}
          >
            <SendIcon />
          </button>
        </div>
      </div>
    )
  }