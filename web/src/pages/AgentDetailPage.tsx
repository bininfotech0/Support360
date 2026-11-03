import { useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { api } from '../api/client'
import { canManage } from '../api/session'
import type { RotateCredentialResponse } from '../api/types'
import { useAuth } from '../auth/authContext'
import {
  EmptyState,
  ErrorBanner,
  Loading,
  PageHeader,
  PageSection,
  ProgressBar,
  Sparkline,
  StatusBadge,
} from '../components/ui'
import { useLoad } from '../hooks/useLoad'
import { useRealtime } from '../hooks/useRealtime'
import {
  copyText,
  formatBytes,
  formatDate,
  formatDuration,
  formatNumber,
  formatPercent,
  metricLabel,
  metricValue,
  relativeTime,
} from '../utils'

export function AgentDetailPage() {
  const { id = '' } = useParams()
  const { user } = useAuth()
  const [range, setRange] = useState('1h')
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const [confirmRevoke, setConfirmRevoke] = useState(false)
  const [rotated, setRotated] = useState<RotateCredentialResponse | null>(null)
  const [copied, setCopied] = useState(false)

  const detail = useLoad(`agent:${id}`, () => api.agent(id))
  const metrics = useLoad(`metrics:${id}:${range}`, () => api.metrics(id, range))
  const connected = useRealtime(() => {
    detail.refresh()
    metrics.refresh()
  })

  const rotate = async () => {
    setBusy(true)
    setError(null)
    try {
      const response = await api.rotateCredential(id)
      setRotated(response)
      detail.refresh()
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : 'Unable to rotate the credential.')
    } finally {
      setBusy(false)
    }
  }

  const revoke = async () => {
    setBusy(true)
    setError(null)
    try {
      await api.revokeAgent(id)
      setConfirmRevoke(false)
      detail.refresh()
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : 'Unable to revoke the agent.')
    } finally {
      setBusy(false)
    }
  }

  const copyKey = async () => {
    if (!rotated) return
    try {
      await copyText(rotated.apiKey)
      setCopied(true)
    } catch {
      setError('Clipboard access was denied. Copy the key manually.')
    }
  }

  if (!id) {
    return <EmptyState title="Agent not found" />
  }

  if (detail.loading && !detail.data) {
    return <Loading label="Loading agent" />
  }

  if (detail.error && !detail.data) {
    return <ErrorBanner message={detail.error} onRetry={detail.refresh} />
  }

  if (!detail.data) {
    return <EmptyState title="Agent not found" />
  }

  const { agent, services, capabilities, credentials, deploymentExecutionAllowed } = detail.data
  const admin = canManage(user)
  const metricKeys = Object.keys(metrics.data?.series ?? {})

  return (
    <>
      <PageHeader
        eyebrow={
          <Link className="text-link" to="/agents">
            ← Back to agents
          </Link>
        }
        title={agent.hostname}
        description={`${agent.platform} · ${agent.osVersion ?? 'OS unknown'} · ${agent.architecture ?? 'arch unknown'} · agent v${agent.version ?? '—'}`}
        actions={
          <>
            <span className={`live-status ${connected ? 'is-live' : ''}`}>
              <span className="live-dot" aria-hidden="true" />
              {connected ? 'Live' : 'Reconnecting'}
            </span>
            <StatusBadge value={agent.status} />
          </>
        }
      />

      {error ? <ErrorBanner message={error} /> : null}
      {detail.error ? <ErrorBanner message={detail.error} onRetry={detail.refresh} /> : null}

      <div className="stats-grid">
        <div className="stat-card">
          <p className="stat-label">Connectivity</p>
          <p className="stat-value status-value">
            <StatusBadge value={agent.connectivity} />
          </p>
          <p className="stat-detail">Heartbeat {relativeTime(agent.lastHeartbeatAt)}</p>
        </div>
        <div className="stat-card">
          <p className="stat-label">CPU</p>
          <p className="stat-value">{formatPercent(agent.cpuPercent)}</p>
          <ProgressBar value={agent.cpuPercent} />
        </div>
        <div className="stat-card">
          <p className="stat-label">Memory</p>
          <p className="stat-value">{formatPercent(agent.memoryPercent)}</p>
          <ProgressBar value={agent.memoryPercent} />
        </div>
        <div className="stat-card">
          <p className="stat-label">Disk</p>
          <p className="stat-value">{formatPercent(agent.diskPercent)}</p>
          <ProgressBar value={agent.diskPercent} />
        </div>
      </div>

      <div className="split-grid">
        <PageSection title="Identity" description="Enrollment and reporting metadata.">
          <dl className="detail-list">
            <div>
              <dt>Agent ID</dt>
              <dd>
                <code>{agent.id}</code>
              </dd>
            </div>
            <div>
              <dt>Enrolled</dt>
              <dd>{formatDate(agent.enrolledAt)}</dd>
            </div>
            <div>
              <dt>Last telemetry</dt>
              <dd>{formatDate(agent.lastTelemetryAt)}</dd>
            </div>
            <div>
              <dt>Uptime</dt>
              <dd>{formatDuration(agent.uptimeSeconds)}</dd>
            </div>
            <div>
              <dt>Collector</dt>
              <dd>
                <StatusBadge value={agent.collectorHealthy ? 'Healthy' : 'Warning'} />
              </dd>
            </div>
            <div>
              <dt>Deployment permission</dt>
              <dd>{deploymentExecutionAllowed ? 'Granted by administrator' : 'Not granted'}</dd>
            </div>
            <div>
              <dt>Local deployment opt-in</dt>
              <dd>{capabilities.includes('deployment_execution') ? 'Reported by agent' : 'Not reported'}</dd>
            </div>
            <div>
              <dt>Active alerts</dt>
              <dd>{agent.activeAlerts}</dd>
            </div>
          </dl>
          <div className="capability-list">
            {capabilities.length === 0 ? (
              <span className="muted">No capabilities reported.</span>
            ) : (
              capabilities.map((capability) => (
                <span key={capability} className="chip">
                  {capability}
                </span>
              ))
            )}
          </div>
        </PageSection>

        <PageSection title="Credential actions" description="Agent keys are hashed at rest and cannot be recovered.">
          {admin ? (
            <div className="danger-zone">
              <div>
                <strong>Rotate API key</strong>
                <p className="muted">Issues a new key and revokes every older credential for this agent.</p>
                <button type="button" className="button button-ghost" onClick={rotate} disabled={busy || agent.status === 'Revoked'}>
                  Rotate credential
                </button>
              </div>
              <div>
                <strong>Revoke agent</strong>
                <p className="muted">Stops all agent access immediately. This action cannot be undone from the console.</p>
                {confirmRevoke ? (
                  <div className="inline-actions">
                    <button type="button" className="button button-danger" onClick={revoke} disabled={busy}>
                      Confirm revoke
                    </button>
                    <button type="button" className="button button-ghost" onClick={() => setConfirmRevoke(false)}>
                      Cancel
                    </button>
                  </div>
                ) : (
                  <button
                    type="button"
                    className="button button-danger"
                    onClick={() => setConfirmRevoke(true)}
                    disabled={busy || agent.status === 'Revoked'}
                  >
                    Revoke agent
                  </button>
                )}
              </div>
            </div>
          ) : (
            <p className="muted">Tenant administrator access is required to rotate or revoke credentials.</p>
          )}

          {rotated ? (
            <div className="secret-callout">
              <strong>New API key — shown once</strong>
              <input readOnly value={rotated.apiKey} onFocus={(event) => event.currentTarget.select()} />
              <div className="inline-actions">
                <button type="button" className="button button-primary" onClick={copyKey}>
                  {copied ? 'Copied' : 'Copy key'}
                </button>
                <button type="button" className="button button-ghost" onClick={() => setRotated(null)}>
                  Dismiss
                </button>
              </div>
            </div>
          ) : null}
        </PageSection>
      </div>

      <PageSection
        title="Telemetry"
        description="Recorded samples for the selected window."
        actions={
          <select value={range} onChange={(event) => setRange(event.target.value)} aria-label="Metrics range">
            <option value="15m">Last 15 minutes</option>
            <option value="1h">Last hour</option>
            <option value="6h">Last 6 hours</option>
            <option value="24h">Last 24 hours</option>
            <option value="7d">Last 7 days</option>
          </select>
        }
      >
        {metrics.error ? <ErrorBanner message={metrics.error} onRetry={metrics.refresh} /> : null}
        {metrics.loading && !metrics.data ? <Loading label="Loading metrics" /> : null}
        {!metrics.loading && metricKeys.length === 0 ? (
          <EmptyState title="No telemetry in this window" detail="Samples appear after the agent reports them." />
        ) : null}
        {metricKeys.length > 0 ? (
          <div className="metric-grid">
            {metricKeys.map((key) => {
              const latest = metrics.data?.latest[key]
              const points = metrics.data?.series[key] ?? []
              return (
                <div key={key} className="metric-card">
                  <div className="metric-heading">
                    <span>{metricLabel(key)}</span>
                    <strong>{latest === undefined ? '—' : metricValue(key, latest)}</strong>
                  </div>
                  <Sparkline points={points} label={metricLabel(key)} />
                  <div className="metric-scale">
                    <span>{formatNumber(Math.min(...points.map((point) => point.value)))}</span>
                    <span>{formatNumber(Math.max(...points.map((point) => point.value)))}</span>
                  </div>
                  {key.includes('.bytes') ? <p className="muted small">Latest: {formatBytes(latest)}</p> : null}
                </div>
              )
            })}
          </div>
        ) : null}
      </PageSection>

      <div className="split-grid">
        <PageSection title="Services" description="Latest service checks reported by the agent.">
          {services.length === 0 ? (
            <EmptyState title="No service checks reported" />
          ) : (
            <div className="table-wrap">
              <table>
                <thead>
                  <tr>
                    <th>Service</th>
                    <th>Kind</th>
                    <th>State</th>
                    <th>Checked</th>
                  </tr>
                </thead>
                <tbody>
                  {services.map((service) => (
                    <tr key={`${service.kind}:${service.name}`}>
                      <td>
                        <strong>{service.name}</strong>
                        {service.detail ? <span className="muted block small">{service.detail}</span> : null}
                      </td>
                      <td>{service.kind}</td>
                      <td>
                        <StatusBadge value={service.state} />
                      </td>
                      <td>{relativeTime(service.checkedAt)}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </PageSection>

        <PageSection title="Credentials" description="Key material is never displayed after creation.">
          {credentials.length === 0 ? (
            <EmptyState title="No credentials" />
          ) : (
            <div className="table-wrap">
              <table>
                <thead>
                  <tr>
                    <th>Prefix</th>
                    <th>Created</th>
                    <th>Expires</th>
                    <th>State</th>
                  </tr>
                </thead>
                <tbody>
                  {credentials.map((credential) => (
                    <tr key={credential.id}>
                      <td>
                        <code>{credential.keyPrefix}…</code>
                      </td>
                      <td>{formatDate(credential.createdAt)}</td>
                      <td>{credential.expiresAt ? formatDate(credential.expiresAt) : 'No expiry'}</td>
                      <td>
                        <StatusBadge
                          value={credential.revokedAt ? 'Revoked' : credential.isExpired ? 'Expired' : 'Active'}
                        />
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </PageSection>
      </div>
    </>
  )
}
