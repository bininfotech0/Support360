import { useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import { api } from '../api/client'
import { canManage } from '../api/session'
import type { EnrollmentCreated } from '../api/types'
import { useAuth } from '../auth/authContext'
import {
  EmptyState,
  ErrorBanner,
  Loading,
  PageHeader,
  PageSection,
  ProgressBar,
  StatusBadge,
} from '../components/ui'
import { useLoad } from '../hooks/useLoad'
import { useRealtime } from '../hooks/useRealtime'
import { copyText, formatDate, formatPercent, relativeTime } from '../utils'

export function AgentsPage() {
  const { user } = useAuth()
  const [search, setSearch] = useState('')
  const [statusFilter, setStatusFilter] = useState('all')
  const [platform, setPlatform] = useState('Linux')
  const [allowDeploymentExecution, setAllowDeploymentExecution] = useState(false)
  const [issuing, setIssuing] = useState(false)
  const [enrollment, setEnrollment] = useState<EnrollmentCreated | null>(null)
  const [actionError, setActionError] = useState<string | null>(null)
  const [copied, setCopied] = useState(false)

  const agents = useLoad('agents', () => api.agents())
  const enrollments = useLoad('enrollments', () => api.enrollments())
  const connected = useRealtime(() => {
    agents.refresh()
    enrollments.refresh()
  })

  const filtered = useMemo(() => {
    const needle = search.trim().toLowerCase()
    return (agents.data ?? []).filter((agent) => {
      const matchesSearch =
        !needle ||
        agent.hostname.toLowerCase().includes(needle) ||
        agent.platform.toLowerCase().includes(needle) ||
        (agent.osVersion ?? '').toLowerCase().includes(needle)
      const matchesStatus = statusFilter === 'all' || agent.status.toLowerCase() === statusFilter
      return matchesSearch && matchesStatus
    })
  }, [agents.data, search, statusFilter])

  const createEnrollment = async () => {
    setIssuing(true)
    setActionError(null)
    setCopied(false)
    try {
      const created = await api.createEnrollment(platform, allowDeploymentExecution)
      setEnrollment(created)
      enrollments.refresh()
    } catch (error) {
      setActionError(error instanceof Error ? error.message : 'Unable to create enrollment.')
    } finally {
      setIssuing(false)
    }
  }

  const copyCommand = async () => {
    if (!enrollment) return
    try {
      await copyText(enrollment.enrollCommand)
      setCopied(true)
    } catch {
      setActionError('Clipboard access was denied. Copy the command manually.')
    }
  }

  return (
    <>
      <PageHeader
        eyebrow="Fleet"
        title="Agents"
        description="Enroll Windows or Linux endpoints and inspect their latest reported state."
        actions={
          <>
            <span className={`live-status ${connected ? 'is-live' : ''}`}>
              <span className="live-dot" aria-hidden="true" />
              {connected ? 'Live' : 'Reconnecting'}
            </span>
            <button type="button" className="button button-ghost" onClick={() => { agents.refresh(); enrollments.refresh() }}>
              Refresh
            </button>
          </>
        }
      />

      {actionError ? <ErrorBanner message={actionError} /> : null}

      {canManage(user) ? (
        <PageSection
          title="Enroll an endpoint"
          description="Enrollment tokens are single-use, expire in 15 minutes, and are shown only once."
        >
          {enrollment ? (
            <div className="enrollment-result">
              <div>
                <strong>Enrollment credential created for {enrollment.targetPlatform?.platform ?? 'any platform'}</strong>
                <p className="muted">Expires {formatDate(enrollment.expiresAt)}. This token cannot be retrieved again.</p>
                <p className="muted">
                  Deployment permission: {enrollment.allowDeploymentExecution ? 'granted by administrator; local opt-in is also required' : 'not granted'}.
                </p>
              </div>
              <label className="secret-field">
                Token
                <input readOnly value={enrollment.token} onFocus={(event) => event.currentTarget.select()} />
              </label>
              <label className="secret-field">
                Agent command
                <input readOnly value={enrollment.enrollCommand} onFocus={(event) => event.currentTarget.select()} />
              </label>
              <div className="inline-actions">
                <button type="button" className="button button-primary" onClick={copyCommand}>
                  {copied ? 'Copied' : 'Copy command'}
                </button>
                <button type="button" className="button button-ghost" onClick={() => setEnrollment(null)}>
                  Done
                </button>
              </div>
            </div>
          ) : (
            <div className="enrollment-controls">
              <label>
                Target platform
                <select value={platform} onChange={(event) => setPlatform(event.target.value)}>
                  <option value="Linux">Linux</option>
                  <option value="Windows">Windows</option>
                </select>
              </label>
              <label className="check-label">
                <input
                  type="checkbox"
                  checked={allowDeploymentExecution}
                  onChange={(event) => setAllowDeploymentExecution(event.target.checked)}
                />
                Grant controlled deployment permission for this enrollment
              </label>
              <p className="muted small">
                Off by default. The local installer must also opt in, and deployment execution is currently disabled.
              </p>
              <button type="button" className="button button-primary" onClick={createEnrollment} disabled={issuing}>
                {issuing ? 'Creating…' : 'Create enrollment token'}
              </button>
            </div>
          )}
        </PageSection>
      ) : null}

      <PageSection
        title="Managed endpoints"
        description={`${filtered.length} of ${agents.data?.length ?? 0} agents shown.`}
        actions={
          <div className="filters">
            <input
              type="search"
              placeholder="Search hostname or platform"
              value={search}
              onChange={(event) => setSearch(event.target.value)}
              aria-label="Search agents"
            />
            <select value={statusFilter} onChange={(event) => setStatusFilter(event.target.value)} aria-label="Filter by status">
              <option value="all">All statuses</option>
              <option value="active">Active</option>
              <option value="revoked">Revoked</option>
            </select>
          </div>
        }
      >
        {agents.loading && !agents.data ? <Loading label="Loading agents" /> : null}
        {agents.error ? <ErrorBanner message={agents.error} onRetry={agents.refresh} /> : null}
        {!agents.loading && filtered.length === 0 ? (
          <EmptyState
            title={agents.data?.length ? 'No agents match the filters' : 'No agents enrolled yet'}
            detail={agents.data?.length ? undefined : 'Create an enrollment token, then run the agent on an endpoint.'}
          />
        ) : null}

        {filtered.length > 0 ? (
          <div className="table-wrap">
            <table>
              <thead>
                <tr>
                  <th>Host</th>
                  <th>Status</th>
                  <th>CPU</th>
                  <th>Memory</th>
                  <th>Disk</th>
                  <th>Alerts</th>
                  <th>Last heartbeat</th>
                </tr>
              </thead>
              <tbody>
                {filtered.map((agent) => (
                  <tr key={agent.id}>
                    <td>
                      <Link className="host-link" to={`/agents/${agent.id}`}>
                        {agent.hostname}
                      </Link>
                      <span className="muted block">
                        {agent.platform} · {agent.osVersion ?? 'OS unknown'} · v{agent.version ?? '—'}
                      </span>
                    </td>
                    <td>
                      <div className="status-stack">
                        <StatusBadge value={agent.status} />
                        <span className="muted small">{agent.connectivity}</span>
                      </div>
                    </td>
                    <td className="metric-cell">
                      <span>{formatPercent(agent.cpuPercent)}</span>
                      <ProgressBar value={agent.cpuPercent} />
                    </td>
                    <td className="metric-cell">
                      <span>{formatPercent(agent.memoryPercent)}</span>
                      <ProgressBar value={agent.memoryPercent} />
                    </td>
                    <td className="metric-cell">
                      <span>{formatPercent(agent.diskPercent)}</span>
                      <ProgressBar value={agent.diskPercent} />
                    </td>
                    <td>{agent.activeAlerts}</td>
                    <td title={formatDate(agent.lastHeartbeatAt)}>{relativeTime(agent.lastHeartbeatAt)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        ) : null}
      </PageSection>

      <PageSection title="Recent enrollment tokens" description="Token values are never stored in plaintext.">
        {enrollments.error ? <ErrorBanner message={enrollments.error} onRetry={enrollments.refresh} /> : null}
        {enrollments.loading && !enrollments.data ? <Loading label="Loading enrollments" /> : null}
        {!enrollments.loading && (enrollments.data?.length ?? 0) === 0 ? (
          <EmptyState title="No enrollment tokens issued" />
        ) : null}
        {(enrollments.data?.length ?? 0) > 0 ? (
          <div className="table-wrap">
            <table>
              <thead>
                <tr>
                  <th>Prefix</th>
                  <th>Platform</th>
                  <th>Deployment grant</th>
                  <th>State</th>
                  <th>Expires</th>
                  <th>Consumed by</th>
                </tr>
              </thead>
              <tbody>
                {enrollments.data?.map((item) => (
                  <tr key={item.id}>
                    <td>
                      <code>{item.tokenPrefix}…</code>
                    </td>
                    <td>{item.targetPlatform ?? 'Any'}</td>
                    <td>{item.allowDeploymentExecution ? 'Granted' : 'Monitoring only'}</td>
                    <td>
                      <StatusBadge
                        value={item.isUsable ? 'Active' : item.consumedAt ? 'Consumed' : 'Expired'}
                        label={item.isUsable ? 'Usable' : item.consumedAt ? 'Consumed' : 'Expired'}
                      />
                    </td>
                    <td>{formatDate(item.expiresAt)}</td>
                    <td>{item.consumedByAgentId ? item.consumedByAgentId.slice(0, 8) : '—'}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        ) : null}
      </PageSection>
    </>
  )
}
