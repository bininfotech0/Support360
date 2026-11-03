import { useState, type FormEvent } from 'react'
import { api } from '../api/client'
import { canOperate } from '../api/session'
import { useAuth } from '../auth/authContext'
import { EmptyState, ErrorBanner, Loading, PageHeader, PageSection, StatusBadge } from '../components/ui'
import { useLoad } from '../hooks/useLoad'
import { formatDate } from '../utils'

const nextStatuses: Record<string, string[]> = {
  New: ['Acknowledged', 'InProgress', 'Resolved'],
  Acknowledged: ['InProgress', 'Resolved'],
  InProgress: ['Resolved'],
  Resolved: ['Closed'],
}

export function IncidentsPage() {
  const { user } = useAuth()
  const incidents = useLoad('incidents', () => api.incidents())
  const alerts = useLoad('incident-alerts', () => api.alerts('Active'))
  const [title, setTitle] = useState('')
  const [description, setDescription] = useState('')
  const [severity, setSeverity] = useState('Medium')
  const [alertId, setAlertId] = useState('')
  const [error, setError] = useState<string | null>(null)

  const create = async (event: FormEvent) => {
    event.preventDefault(); setError(null)
    try {
      await api.createIncident({ title, description, severity, alertIds: alertId ? [alertId] : [], relatedBatchJobExecutionId: null })
      setTitle(''); setDescription(''); setAlertId(''); incidents.refresh()
    } catch (e) { setError(e instanceof Error ? e.message : 'Could not create incident.') }
  }

  const transition = async (id: string, status: string) => {
    setError(null)
    try { await api.transitionIncident(id, status); incidents.refresh() }
    catch (e) { setError(e instanceof Error ? e.message : 'Could not update incident.') }
  }

  return <>
    <PageHeader eyebrow="Operations" title="Incidents" description="Track incidents, their linked alerts, and related approved changes." actions={<button type="button" className="button button-ghost" onClick={() => { incidents.refresh(); alerts.refresh() }}>Refresh</button>} />
    {error ? <ErrorBanner message={error} /> : null}
    {canOperate(user) ? <PageSection title="Create incident" description="Link an active alert when it triggered or contributed to the incident.">
      <form className="enrollment-controls" onSubmit={create}>
        <label>Title<input required maxLength={200} value={title} onChange={e => setTitle(e.target.value)} /></label>
        <label>Severity<select value={severity} onChange={e => setSeverity(e.target.value)}><option>Low</option><option>Medium</option><option>High</option><option>Critical</option></select></label>
        <label>Link active alert<select value={alertId} onChange={e => setAlertId(e.target.value)}><option value="">No alert</option>{alerts.data?.map(alert => <option key={alert.id} value={alert.id}>{alert.hostname} · {alert.ruleName}</option>)}</select></label>
        <label>Description<textarea required maxLength={8000} value={description} onChange={e => setDescription(e.target.value)} /></label>
        <button type="submit" className="button button-primary">Create incident</button>
      </form>
    </PageSection> : null}
    <PageSection title="Incident register" description={`${incidents.data?.length ?? 0} incidents`}>
      {incidents.loading && !incidents.data ? <Loading label="Loading incidents" /> : null}
      {incidents.error ? <ErrorBanner message={incidents.error} onRetry={incidents.refresh} /> : null}
      {!incidents.loading && incidents.data?.length === 0 ? <EmptyState title="No incidents recorded" detail="Create an incident when an operational issue needs coordinated investigation." /> : null}
      {!!incidents.data?.length ? <div className="table-wrap"><table><thead><tr><th>Incident</th><th>Severity</th><th>Status</th><th>Alerts</th><th>Related changes</th><th>Opened</th><th>Actions</th></tr></thead><tbody>{incidents.data.map(incident => <tr key={incident.id}><td><strong>{incident.number}</strong><span className="muted block">{incident.title}</span><span className="muted block">{incident.description}</span></td><td><StatusBadge value={incident.severity} /></td><td><StatusBadge value={incident.status} /></td><td>{incident.alerts.length ? incident.alerts.map(alert => <span className="block" key={alert.alertId}>{alert.message}</span>) : '—'}</td><td>{incident.relatedChangeRequestIds.length ? incident.relatedChangeRequestIds.length : '—'}</td><td>{formatDate(incident.openedAt)}</td><td>{canOperate(user) ? <div className="inline-actions">{(nextStatuses[incident.status] ?? []).map(status => <button key={status} type="button" className="button button-ghost" onClick={() => transition(incident.id, status)}>{status === 'InProgress' ? 'Start work' : status}</button>)}</div> : '—'}</td></tr>)}</tbody></table></div> : null}
    </PageSection>
  </>
}
