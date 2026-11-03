import { useState, type FormEvent } from 'react'
import { api } from '../api/client'
import { canOperate } from '../api/session'
import type { ApplicationCheck, ApplicationCheckHistoryItem } from '../api/types'
import { useAuth } from '../auth/authContext'
import { EmptyState, ErrorBanner, Loading, PageHeader, PageSection, StatusBadge } from '../components/ui'
import { useLoad } from '../hooks/useLoad'
import { formatDate } from '../utils'

export function ApplicationsPage() {
  const { user } = useAuth()
  const checks = useLoad('application-checks', () => api.applicationChecks())
  const agents = useLoad('application-check-agents', () => api.agents())
  const [name, setName] = useState('')
  const [agentId, setAgentId] = useState('')
  const [type, setType] = useState('Http')
  const [target, setTarget] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [editing, setEditing] = useState<ApplicationCheck | null>(null)
  const [history, setHistory] = useState<{ id: string; items: ApplicationCheckHistoryItem[] } | null>(null)
  const [historyLoading, setHistoryLoading] = useState(false)
  const refresh = () => { checks.refresh(); agents.refresh() }

  const create = async (event: FormEvent) => {
    event.preventDefault(); setError(null)
    try {
      await api.createApplicationCheck({ agentId, name, type, target, intervalSeconds: 60, enabled: true })
      setName(''); setTarget(''); checks.refresh()
    } catch (e) { setError(e instanceof Error ? e.message : 'Could not create application check.') }
  }

  const save = async (event: FormEvent) => {
    event.preventDefault()
    if (!editing) return
    setError(null)
    try {
      await api.updateApplicationCheck(editing.id, {
        name: editing.name, type: editing.type, target: editing.target,
        intervalSeconds: editing.intervalSeconds, enabled: editing.enabled,
      })
      setEditing(null); checks.refresh()
    } catch (e) { setError(e instanceof Error ? e.message : 'Could not update application check.') }
  }

  const remove = async (id: string) => {
    try { await api.deleteApplicationCheck(id); checks.refresh(); if (history?.id === id) setHistory(null) }
    catch (e) { setError(e instanceof Error ? e.message : 'Could not remove application check.') }
  }

  const showHistory = async (id: string) => {
    setHistoryLoading(true); setError(null)
    try { setHistory({ id, items: await api.applicationCheckHistory(id) }) }
    catch (e) { setError(e instanceof Error ? e.message : 'Could not load check history.') }
    finally { setHistoryLoading(false) }
  }

  return <>
    <PageHeader eyebrow="Monitoring" title="Application checks" description="Configure endpoint checks and review results reported by monitored agents." actions={<button type="button" className="button button-ghost" onClick={refresh}>Refresh</button>} />
    {error ? <ErrorBanner message={error} /> : null}
    {canOperate(user) && !editing ? <PageSection title="Add a check" description="HTTP checks accept an absolute HTTP or HTTPS URL. Process and service checks run on the selected agent.">
      <form className="enrollment-controls" onSubmit={create}>
        <label>Name<input required maxLength={120} value={name} onChange={e => setName(e.target.value)} /></label>
        <label>Agent<select required value={agentId} onChange={e => setAgentId(e.target.value)}><option value="">Select agent</option>{agents.data?.filter(a => a.status === 'Active').map(a => <option key={a.id} value={a.id}>{a.hostname}</option>)}</select></label>
        <label>Check type<select value={type} onChange={e => setType(e.target.value)}><option>Http</option><option>Process</option><option>WindowsService</option><option>SystemdService</option></select>{type === 'Process' ? <span className="muted small">Reports process presence only; it does not confirm application health.</span> : null}</label>
        <label>Target<input required value={target} onChange={e => setTarget(e.target.value)} placeholder={type === 'Http' ? 'https://service.example/health' : 'service or process name'} /></label>
        <button type="submit" className="button button-primary" disabled={!agentId}>Create check</button>
      </form>
    </PageSection> : null}
    {editing ? <PageSection title={`Edit ${editing.name}`}>
      <form className="enrollment-controls" onSubmit={save}>
        <label>Name<input required maxLength={120} value={editing.name} onChange={e => setEditing({ ...editing, name: e.target.value })} /></label>
        <label>Check type<select value={editing.type} onChange={e => setEditing({ ...editing, type: e.target.value })}><option>Http</option><option>Process</option><option>WindowsService</option><option>SystemdService</option></select></label>
        <label>Target<input required value={editing.target} onChange={e => setEditing({ ...editing, target: e.target.value })} /></label>
        <label>Interval (seconds)<input type="number" min={30} max={86400} value={editing.intervalSeconds} onChange={e => setEditing({ ...editing, intervalSeconds: Number(e.target.value) })} /></label>
        <label><input type="checkbox" checked={editing.enabled} onChange={e => setEditing({ ...editing, enabled: e.target.checked })} /> Enabled</label>
        <button type="submit" className="button button-primary">Save changes</button>
        <button type="button" className="button button-ghost" onClick={() => setEditing(null)}>Cancel</button>
      </form>
    </PageSection> : null}
    <PageSection title="Configured checks" description={`${checks.data?.length ?? 0} checks`}>
      {checks.loading && !checks.data ? <Loading label="Loading checks" /> : null}
      {checks.error ? <ErrorBanner message={checks.error} onRetry={checks.refresh} /> : null}
      {!checks.loading && checks.data?.length === 0 ? <EmptyState title="No application checks configured" detail="Create a check to start collecting application health results." /> : null}
      {!!checks.data?.length ? <div className="table-wrap"><table><thead><tr><th>Check</th><th>Agent</th><th>Target</th><th>Status</th><th>Last checked</th><th>Actions</th></tr></thead><tbody>{checks.data.map(c => <tr key={c.id}><td>{c.name}<span className="muted block">{c.type} · every {c.intervalSeconds}s</span></td><td>{c.hostname}</td><td>{c.target}</td><td><StatusBadge value={c.enabled ? c.lastStatus : 'Disabled'} /></td><td>{formatDate(c.lastExecutedAt)}</td><td><div className="inline-actions"><button type="button" className="button button-ghost" onClick={() => showHistory(c.id)}>History</button>{canOperate(user) ? <><button type="button" className="button button-ghost" onClick={() => setEditing(c)}>Edit</button><button type="button" className="button button-ghost" onClick={() => remove(c.id)}>Delete</button></> : null}</div></td></tr>)}</tbody></table></div> : null}
    </PageSection>
    {historyLoading ? <Loading label="Loading check history" /> : null}
    {history ? <PageSection title="Check history" description="Most recent results returned by the agent.">
      {history.items.length === 0 ? <EmptyState title="No results yet" /> : <div className="table-wrap"><table><thead><tr><th>Status</th><th>Started</th><th>Duration</th><th>Error</th></tr></thead><tbody>{history.items.map(item => <tr key={item.eventId}><td><StatusBadge value={item.status} /></td><td>{formatDate(item.startedAt)}</td><td>{item.durationMilliseconds} ms</td><td>{item.errorCategory ? `${item.errorCategory}: ${item.errorDetail ?? ''}` : '—'}</td></tr>)}</tbody></table></div>}
    </PageSection> : null}
  </>
}
