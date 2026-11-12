import { Link } from 'react-router-dom'
import { api } from '../api/client'
import {
  EmptyState,
  ErrorBanner,
  LiveStatus,
  Loading,
  PageHeader,
  PageSection,
  StatCard,
  StatusBadge,
} from '../components/ui'
import { useLoad } from '../hooks/useLoad'
import { useRealtime } from '../hooks/useRealtime'
import { formatDate, relativeTime } from '../utils'

export function DashboardPage() {
  const overview = useLoad('overview', () => api.overview())
  const connected = useRealtime(overview.refresh)

  if (overview.loading && !overview.data) {
    return <Loading label="Loading overview" />
  }

  if (overview.error && !overview.data) {
    return <ErrorBanner message={overview.error} onRetry={overview.refresh} />
  }

  if (!overview.data) {
    return <EmptyState title="No overview available" />
  }

  const data = overview.data
  const coverageTone = data.monitoringCoveragePercent >= 90 ? 'ok' : data.monitoringCoveragePercent >= 60 ? 'warn' : 'bad'

  return (
    <>
      <PageHeader
        eyebrow="Operations"
        title="Operations overview"
        description="Live health of your tenant fleet. Data refreshes automatically when agents report in."
        actions={
          <>
            <LiveStatus connected={connected} />
            <button type="button" className="button button-ghost" onClick={overview.refresh}>
              Refresh
            </button>
          </>
        }
      />

      {overview.error ? <ErrorBanner message={overview.error} onRetry={overview.refresh} /> : null}

      {!data.deploymentExecutionEnabled ? (
        <div className="notice">
          <strong>Deployment execution is disabled.</strong>
          <span>
            Monitoring and enrollment are active. Deployment controls stay unavailable until an administrator
            enables signed, policy-controlled execution.
          </span>
        </div>
      ) : null}

      <div className="stats-grid">
        <StatCard label="Total agents" value={data.totalAgents} detail={`${data.onlineAgents} online`} />
        <StatCard
          label="Online"
          value={data.onlineAgents}
          detail={`${data.offlineAgents} offline`}
          tone={data.offlineAgents > 0 ? 'warn' : 'ok'}
        />
        <StatCard
          label="Active alerts"
          value={data.activeAlerts}
          detail={`${data.criticalAlerts} critical`}
          tone={data.criticalAlerts > 0 ? 'bad' : data.activeAlerts > 0 ? 'warn' : 'ok'}
        />
        <StatCard
          label="Monitoring coverage"
          value={`${data.monitoringCoveragePercent}%`}
          detail="Agents reporting within the heartbeat window"
          tone={coverageTone}
        />
      </div>

      <div className="split-grid">
        <PageSection
          title="Recent alerts"
          description="Newest alerts for this tenant."
          actions={
            <Link className="text-link" to="/alerts">
              View all
            </Link>
          }
        >
          {data.recentAlerts.length === 0 ? (
            <EmptyState title="No active alerts" detail="Alert rules are evaluated in the background." />
          ) : (
            <div className="stack">
              {data.recentAlerts.map((alert) => (
                <div key={alert.id} className="list-row">
                  <div>
                    <div className="row-title">
                      <span className={`severity severity-${alert.severity.toLowerCase()}`}>{alert.severity}</span>
                      <strong>{alert.ruleName}</strong>
                      <StatusBadge value={alert.status} />
                    </div>
                    <p className="row-detail">{alert.message}</p>
                    <p className="row-meta">
                      {alert.hostname} · {relativeTime(alert.triggeredAt)}
                    </p>
                  </div>
                  <span className="row-value">{alert.triggeredValue.toFixed(1)}</span>
                </div>
              ))}
            </div>
          )}
        </PageSection>

        <PageSection title="Coverage" description="Share of agents reporting inside the liveness window.">
          <div className="coverage-block">
            <div className={`coverage-ring tone-${coverageTone}`}>
              <svg viewBox="0 0 120 120" role="img" aria-label={`${data.monitoringCoveragePercent}% coverage`}>
                <circle cx="60" cy="60" r="52" className="ring-track" />
                <circle
                  cx="60"
                  cy="60"
                  r="52"
                  className="ring-value"
                  strokeDasharray={`${(data.monitoringCoveragePercent / 100) * 326.7} 326.7`}
                />
              </svg>
              <span>{data.monitoringCoveragePercent}%</span>
            </div>
            <div className="coverage-copy">
              <p>
                {data.onlineAgents} of {data.totalAgents} agents online
              </p>
              <p className="muted">Deployments in progress: {data.activeDeployments}</p>
              <p className="muted">Generated {formatDate(data.generatedAt)}</p>
            </div>
          </div>
        </PageSection>
      </div>

      <PageSection title="Recent activity" description="Security-relevant events recorded for this tenant.">
        {data.recentEvents.length === 0 ? (
          <EmptyState title="No activity yet" detail="Enrollment, credential, and alert actions appear here." />
        ) : (
          <div className="table-wrap">
            <table>
              <thead>
                <tr>
                  <th>Action</th>
                  <th>Actor</th>
                  <th>Target</th>
                  <th>Outcome</th>
                  <th>When</th>
                </tr>
              </thead>
              <tbody>
                {data.recentEvents.map((event) => (
                  <tr key={event.id}>
                    <td>
                      <code>{event.action}</code>
                    </td>
                    <td>{event.actorDisplay ?? event.actorType}</td>
                    <td>
                      {event.targetType}
                      {event.targetId ? <span className="muted"> · {event.targetId.slice(0, 8)}</span> : null}
                    </td>
                    <td>
                      <StatusBadge value={event.outcome} />
                    </td>
                    <td title={formatDate(event.createdAt)}>{relativeTime(event.createdAt)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </PageSection>
    </>
  )
}
