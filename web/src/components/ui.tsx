import type { ReactNode } from 'react'
import type { AgentMetricPoint } from '../api/types'
import { statusTone } from '../utils'

export function PageHeader({
  eyebrow,
  title,
  description,
  actions,
}: {
  eyebrow?: ReactNode
  title: string
  description?: string
  actions?: ReactNode
}) {
  return (
    <div className="page-header">
      <div>
        {eyebrow ? <p className="eyebrow">{eyebrow}</p> : null}
        <h1>{title}</h1>
        {description ? <p className="page-description">{description}</p> : null}
      </div>
      {actions ? <div className="page-actions">{actions}</div> : null}
    </div>
  )
}

export function StatCard({
  label,
  value,
  detail,
  tone,
}: {
  label: string
  value: string | number
  detail?: string
  tone?: 'ok' | 'warn' | 'bad' | 'neutral'
}) {
  return (
    <div className={`stat-card ${tone ? `tone-${tone}` : ''}`}>
      <p className="stat-label">{label}</p>
      <p className="stat-value">{value}</p>
      {detail ? <p className="stat-detail">{detail}</p> : null}
    </div>
  )
}

export function StatusBadge({ value, label }: { value: string | null | undefined; label?: string }) {
  const tone = statusTone(value)
  return (
    <span className={`badge badge-${tone}`}>
      <span className="badge-dot" aria-hidden="true" />
      {label ?? value ?? 'Unknown'}
    </span>
  )
}

export function ProgressBar({ value, max = 100 }: { value: number | null; max?: number }) {
  const percent = value === null ? 0 : Math.max(0, Math.min(100, (value / max) * 100))
  const tone = percent >= 90 ? 'bad' : percent >= 75 ? 'warn' : 'ok'
  return (
    <div className={`meter meter-${tone}`}>
      <span style={{ width: `${percent}%` }} />
    </div>
  )
}

export function Sparkline({
  points,
  label,
  height = 48,
}: {
  points: AgentMetricPoint[]
  label: string
  height?: number
}) {
  if (points.length === 0) {
    return <div className="sparkline-empty">No samples in range</div>
  }

  const width = 180
  const values = points.map((point) => point.value)
  const min = Math.min(...values)
  const max = Math.max(...values)
  const spread = max - min || 1
  const step = points.length > 1 ? width / (points.length - 1) : width
  const coords = points
    .map((point, index) => {
      const x = index * step
      const y = height - ((point.value - min) / spread) * (height - 6) - 3
      return `${x.toFixed(2)},${y.toFixed(2)}`
    })
    .join(' ')

  return (
    <svg
      className="sparkline"
      viewBox={`0 0 ${width} ${height}`}
      preserveAspectRatio="none"
      role="img"
      aria-label={`${label} trend`}
    >
      <title>{label} trend</title>
      <polyline points={coords} fill="none" strokeWidth="2" vectorEffect="non-scaling-stroke" />
    </svg>
  )
}

export function LiveStatus({ connected }: { connected: boolean }) {
  return (
    <span className={`live-status ${connected ? 'is-live' : ''}`} role="status" aria-live="polite">
      <span className="live-dot" aria-hidden="true" />
      {connected ? 'Live' : 'Reconnecting'}
    </span>
  )
}

export function Loading({ label = 'Loading' }: { label?: string }) {
  return (
    <div className="loading-state" role="status">
      <span className="spinner" aria-hidden="true" />
      {label}
    </div>
  )
}

export function EmptyState({ title, detail }: { title: string; detail?: string }) {
  return (
    <div className="empty-state">
      <p>{title}</p>
      {detail ? <span>{detail}</span> : null}
    </div>
  )
}

export function ErrorBanner({ message, onRetry }: { message: string; onRetry?: () => void }) {
  return (
    <div className="error-banner" role="alert">
      <span>{message}</span>
      {onRetry ? (
        <button type="button" className="button button-ghost" onClick={onRetry}>
          Retry
        </button>
      ) : null}
    </div>
  )
}

export function PageSection({
  title,
  description,
  actions,
  children,
}: {
  title: string
  description?: string
  actions?: ReactNode
  children: ReactNode
}) {
  return (
    <section className="panel">
      <div className="panel-heading">
        <div>
          <h2>{title}</h2>
          {description ? <p>{description}</p> : null}
        </div>
        {actions}
      </div>
      {children}
    </section>
  )
}
