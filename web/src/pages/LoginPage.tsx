import { useState, type FormEvent } from 'react'
import { Navigate, useLocation, useNavigate } from 'react-router-dom'
import { useAuth } from '../auth/authContext'

export function LoginPage() {
  const { session, login } = useAuth()
  const navigate = useNavigate()
  const location = useLocation()
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [submitting, setSubmitting] = useState(false)

  if (session) {
    const target = (location.state as { from?: string } | null)?.from ?? '/'
    return <Navigate to={target} replace />
  }

  const submit = async (event: FormEvent) => {
    event.preventDefault()
    setError(null)
    setSubmitting(true)
    try {
      await login(email.trim(), password)
      const target = (location.state as { from?: string } | null)?.from ?? '/'
      navigate(target, { replace: true })
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : 'Sign-in failed.')
    } finally {
      setSubmitting(false)
    }
  }

  return (
    <div className="login-page">
      <div className="login-brand">
        <span className="brand-mark large" aria-hidden="true">
          S3
        </span>
        <p className="eyebrow">Support360</p>
        <h1>Operate every endpoint from one secure console.</h1>
        <p>
          Monitoring, alerting, and tenant-isolated agent management for Windows and Linux fleets.
        </p>
        <ul className="login-points">
          <li>Tenant-scoped data access enforced server-side</li>
          <li>Short-lived enrollment credentials and rotatable agent keys</li>
          <li>Telemetry, service health, batch jobs, and audit history</li>
        </ul>
      </div>

      <div className="login-panel">
        <form onSubmit={submit} className="login-form">
          <div>
            <p className="eyebrow">Sign in</p>
            <h2>Operations console</h2>
            <p className="muted">Use your Support360 account.</p>
          </div>

          {error ? (
            <div className="error-banner" role="alert">
              {error}
            </div>
          ) : null}

          <label>
            Email
            <input
              type="email"
              autoComplete="username"
              value={email}
              onChange={(event) => setEmail(event.target.value)}
              required
            />
          </label>

          <label>
            Password
            <input
              type="password"
              autoComplete="current-password"
              value={password}
              onChange={(event) => setPassword(event.target.value)}
              required
            />
          </label>

          <button type="submit" className="button button-primary" disabled={submitting}>
            {submitting ? 'Signing in…' : 'Sign in'}
          </button>

          {import.meta.env.DEV ? (
            <div className="dev-hint">
              <strong>Local demo seed</strong>
              <span>admin@support360.local</span>
              <span>DevOnly-ChangeMe-123!</span>
              <small>Available only when the Development seed is enabled.</small>
            </div>
          ) : null}
        </form>
      </div>
    </div>
  )
}
