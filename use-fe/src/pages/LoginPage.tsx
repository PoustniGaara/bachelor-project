import { useAuth } from '../auth/useAuth'

export default function LoginPage() {
  const { mode, login, configError } = useAuth()

  const isMsal = mode === 'msal'
  const buttonLabel = isMsal ? 'Prihlásiť sa cez Microsoft' : 'Pokračovať ako vývojársky používateľ'

  const buttonStyle: React.CSSProperties = {
    width: '100%',
    padding: '0.85rem 1rem',
    borderRadius: '12px',
    border: 'none',
    background: configError ? '#3a3a3a' : '#ececec',
    color: configError ? '#8a8a8a' : '#171717',
    fontSize: '0.95rem',
    fontWeight: 600,
    cursor: configError ? 'not-allowed' : 'pointer',
  }

  return (
    <div
      style={{
        height: '100vh',
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
        background: '#212121',
        color: '#ececec',
        fontFamily: 'system-ui, sans-serif',
        position: 'relative',
        overflow: 'hidden',
      }}
    >
      {/* Brand glow, consistent with the chat empty state */}
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
          maxWidth: 380,
          padding: '2rem',
          background: '#171717',
          border: '1px solid #2f2f2f',
          borderRadius: '16px',
          textAlign: 'center',
        }}
      >
        <img
          src="USE_logo_lockup_transparent.png"
          alt="Unified Search Engine"
          style={{ height: 48, width: 'auto', objectFit: 'contain', marginBottom: '1.5rem' }}
        />

        <h1 style={{ fontSize: '1.4rem', fontWeight: 600, margin: '0 0 0.5rem' }}>
          AI asistent pre dokumentáciu
        </h1>
        <p style={{ color: '#9b9b9b', fontSize: '0.9rem', margin: '0 0 1.75rem' }}>
          {isMsal
            ? 'Prihláste sa pomocou firemného Microsoft konta.'
            : 'Vývojársky režim — pokračujte bez prihlásenia.'}
        </p>

        {configError && (
          <div
            style={{
              marginBottom: '1.25rem',
              padding: '0.85rem',
              border: '1px solid #5c2b2b',
              background: '#3a1f1f',
              color: '#f5b5b5',
              borderRadius: '12px',
              fontSize: '0.85rem',
              textAlign: 'left',
            }}
          >
            {configError}
          </div>
        )}

        <button onClick={() => void login()} disabled={Boolean(configError)} style={buttonStyle}>
          {buttonLabel}
        </button>

        <p style={{ color: '#7d7d7d', fontSize: '0.75rem', marginTop: '1.25rem' }}>
          Režim prihlásenia: <strong>{mode}</strong>
        </p>
      </div>
    </div>
  )
}