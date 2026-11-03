import { useState, type FormEvent } from 'react'
import { api } from '../api/client'
import { canManage, canOperate } from '../api/session'
import { useAuth } from '../auth/authContext'
import {
  EmptyState,
  ErrorBanner,
  Loading,
  PageHeader,
  PageSection,
  StatusBadge,
} from '../components/ui'
import { useLoad } from '../hooks/useLoad'
import { useRealtime } from '../hooks/useRealtime'
import { formatDate, formatNumber, metricLabel, relativeTime } from '../utils'

const metrics = [
  'cpu.usage.percent',
  'memory.used.percent',
  'disk.used.percent',
  'disk.free.bytes',
  'memory.used.bytes',
  'uptime.seconds',
]

const severities = ['Critical', 'Warning', 'Info']

export function AlertsPage() {
  const { user } = useAuth()
  const [status, setStatus] = useState('active')
  const [actionError, setActionError] = useState<string | null>(null)
  const [busyId, setBusyId] = useState<string | null>(null)
  const [showRuleForm, setShowRuleForm] = useState(false)
  const [rule, setRule] = useState({
    name: '',
    metric: 'cpu.usage.percent',
    operator: 'GreaterThan',
    threshold: 85,
    severity: 'Warning',
    consecutiveBreaches: 3,
  })

  const alerts = useLoad(`alerts:${status}`, () => api.alerts(status === 'all' ? undefined : status))
  const rules = useLoad('alert-rules', () => api.alertRules())
  const connected = useRealtime(() => {
    alerts.refresh()
    rules.refresh()
  })

  const acknowledge = async (id: string) => {
    setBusyId(id)
    setActionError(null)
    try {
      await api.acknowledgeAlert(id)
      alerts.refresh()
    } catch (caught) {
      setActionError(caught instanceof Error ? caught.message : 'Unable to acknowledge the alert.')
    } finally {
      setBusyId(null)
    }
  }

  const submitRule = async (event: FormEvent) => {
    event.preventDefault()
    setActionError(null)
    try {
      await api.createAlertRule(rule)
      setShowRuleForm(false)
      setRule({
        name: '',
        metric: 'cpu.usage.percent',
        operator: 'GreaterThan',
        threshold: 85,
        severity: 'Warning',
        consecutiveBreaches: 3,
      })
      rules.refresh()
      alerts.refresh()
    } catch (caught) {
      setActionError(caught instanceof Error ? caught.message : 'Unable to create the alert rule.')
    }
  }

  return (
    <>
      <PageHeader
        eyebrow="Reliability"
        title="Alerts"
        description="Threshold breaches evaluated by the background alert worker."
        actions={
          <>
            <span className={`live-status ${connected ? 'is-live' : ''}`}>
              <span className="live-dot" aria-hidden="true" />
              {connected ? 'Live' : 'Reconnecting'}
            </span>
            {canManage(user) ? (
              <button type="button" className="button button-primary" onClick={() => setShowRuleForm((value) => !value)}>
                {showRuleForm ? 'Close rule form' : 'New rule'}
              </button>
            ) : null}
          </>
        }
      />

      {actionError ? <ErrorBanner message={actionError} /> : null}

      {showRuleForm && canManage(user) ? (
        <PageSection title="Create alert rule" description="Rules apply to every agent in this tenant.">
          <form className="form-grid" onSubmit={submitRule}>
            <label>
              Rule name
              <input
                value={rule.name}
                onChange={(event) => setRule({ ...rule, name: event.target.value })}
                required
                maxLength={160}
              />
            </label>
            <label>
              Metric
              <select value={rule.metric} onChange={(event) => setRule({ ...rule, metric: event.target.value })}>
                {metrics.map((metric) => (
                  <option key={metric} value={metric}>
                    {metricLabel(metric)}
                  </option>
                ))}
              </select>
            </label>
            <label>
              Operator
              <select value={rule.operator} onChange={(event) => setRule({ ...rule, operator: event.target.value })}>
                <option value="GreaterThan">Greater than</option>
                <option value="LessThan">Less than</option>
              </select>
            </label>
            <label>
              Threshold
              <input
                type="number"
                step="any"
                value={rule.threshold}
                onChange={(event) => setRule({ ...rule, threshold: Number(event.target.value) })}
                required
              />
            </label>
            <label>
              Severity
              <select value={rule.severity} onChange={(event) => setRule({ ...rule, severity: event.target.value })}>
                {severities.map((severity) => (
                  <option key={severity} value={severity}>
                    {severity}
                  </option>
                ))}
              </select>
            </label>
            <label>
              Consecutive breaches
              <input
                type="number"
                min={1}
                max={20}
                value={rule.consecutiveBreaches}
                onChange={(event) => setRule({ ...rule, consecutiveBreaches: Number(event.target.value) })}
                required
              />
            </label>
            <div className="form-actions">
              <button type="submit" className="button button-primary">
                Create rule
              </button>
            </div>
          </form>
        </PageSection>
      ) : null}

      <PageSection
        title="Alert queue"
        description={`${alerts.data?.length ?? 0} alerts in the selected view.`}
        actions={
          <select value={status} onChange={(event) => setStatus(event.target.value)} aria-label="Alert status filter">
            <option value="active">Active</option>
            <option value="acknowledged">Acknowledged</option>
            <option value="resolved">Resolved</option>
            <option value="all">All</option>
          </select>
        }
      >
        {alerts.loading && !alerts.data ? <Loading label="Loading alerts" /> : null}
        {alerts.error ? <ErrorBanner message={alerts.error} onRetry={alerts.refresh} /> : null}
        {!alerts.loading && (alerts.data?.length ?? 0) === 0 ? (
          <EmptyState
            title="No alerts in this view"
            detail="Create a rule or wait for the next evaluation cycle."
          />
        ) : null}
        {(alerts.data?.length ?? 0) > 0 ? (
          <div className="alert-list">
            {alerts.data?.map((alert) => (
              <article key={alert.id} className={`alert-card severity-border-${alert.severity.toLowerCase()}`}>
                <div className="alert-main">
                  <div className="row-title">
                    <span className={`severity severity-${alert.severity.toLowerCase()}`}>{alert.severity}</span>
                    <strong>{alert.ruleName}</strong>
                    <StatusBadge value={alert.status} />
                  </div>
                  <p>{alert.message}</p>
                  <p className="row-meta">
                    {alert.hostname} · {metricLabel(alert.metric)} · observed {formatNumber(alert.triggeredValue)} ·{' '}
                    {relativeTime(alert.triggeredAt)}
                  </p>
                </div>
                <div className="alert-actions">
                  <span className="muted small">{formatDate(alert.triggeredAt)}</span>
                  {alert.status.toLowerCase() === 'active' && canOperate(user) ? (
                    <button
                      type="button"
                      className="button button-ghost"
                      onClick={() => acknowledge(alert.id)}
                      disabled={busyId === alert.id}
                    >
                      {busyId === alert.id ? 'Acknowledging…' : 'Acknowledge'}
                    </button>
                  ) : null}
                </div>
              </article>
            ))}
          </div>
        ) : null}
      </PageSection>

      <PageSection title="Alert rules" description="Tenant-level thresholds. Changes apply on the next evaluation.">
        {rules.loading && !rules.data ? <Loading label="Loading rules" /> : null}
        {rules.error ? <ErrorBanner message={rules.error} onRetry={rules.refresh} /> : null}
        {!rules.loading && (rules.data?.length ?? 0) === 0 ? <EmptyState title="No alert rules configured" /> : null}
        {(rules.data?.length ?? 0) > 0 ? (
          <div className="table-wrap">
            <table>
              <thead>
                <tr>
                  <th>Name</th>
                  <th>Metric</th>
                  <th>Condition</th>
                  <th>Severity</th>
                  <th>Breaches</th>
                  <th>State</th>
                </tr>
              </thead>
              <tbody>
                {rules.data?.map((item) => (
                  <tr key={item.id}>
                    <td>
                      <strong>{item.name}</strong>
                    </td>
                    <td>{metricLabel(item.metric)}</td>
                    <td>
                      {item.operator === 'GreaterThan' ? '>' : '<'} {formatNumber(item.threshold)}
                    </td>
                    <td>
                      <span className={`severity severity-${item.severity.toLowerCase()}`}>{item.severity}</span>
                    </td>
                    <td>{item.consecutiveBreaches}</td>
                    <td>
                      <StatusBadge value={item.enabled ? 'Enabled' : 'Disabled'} label={item.enabled ? 'Enabled' : 'Disabled'} />
                    </td>
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
