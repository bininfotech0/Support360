import { useState, type FormEvent } from 'react'
import { api } from '../api/client'
import { canApprove, canManage, canOperate } from '../api/session'
import type { ChangeRequest } from '../api/types'
import { useAuth } from '../auth/authContext'
import { EmptyState, ErrorBanner, Loading, PageHeader, PageSection, StatusBadge } from '../components/ui'
import { useLoad } from '../hooks/useLoad'
import { formatDate } from '../utils'

function localInput(hoursFromNow: number) {
  const date = new Date(Date.now() + hoursFromNow * 60 * 60 * 1000)
  date.setMinutes(date.getMinutes() - date.getTimezoneOffset())
  return date.toISOString().slice(0, 16)
}

export function ChangeRequestsPage() {
  const { user } = useAuth()
  const changes = useLoad('change-requests', () => api.changeRequests())
  const incidents = useLoad('change-incidents', () => api.incidents())
  const policy = useLoad('change-policy', () => api.changeApprovalPolicy())
  const [title, setTitle] = useState('')
  const [description, setDescription] = useState('')
  const [risk, setRisk] = useState('')
  const [environment, setEnvironment] = useState('production')
  const [start, setStart] = useState(() => localInput(2))
  const [end, setEnd] = useState(() => localInput(3))
  const [incidentId, setIncidentId] = useState('')
  const [requiredApprovals, setRequiredApprovals] = useState<number | null>(null)
  const [separation, setSeparation] = useState<boolean | null>(null)
  const [comments, setComments] = useState<Record<string, string>>({})
  const [error, setError] = useState<string | null>(null)
  const refresh = () => { changes.refresh(); incidents.refresh(); policy.refresh() }

  const create = async (event: FormEvent) => {
    event.preventDefault(); setError(null)
    try {
      await api.createChangeRequest({
        title, description, risk, targetEnvironment: environment,
        plannedStartAt: new Date(start).toISOString(), plannedEndAt: new Date(end).toISOString(),
        relatedIncidentId: incidentId || null,
      })
      setTitle(''); setDescription(''); setRisk(''); setIncidentId(''); changes.refresh()
    } catch (e) { setError(e instanceof Error ? e.message : 'Could not create change request.') }
  }

  const updatePolicy = async (event: FormEvent) => {
    event.preventDefault(); setError(null)
    try {
      await api.updateChangeApprovalPolicy({
        requiredApprovals: requiredApprovals ?? policy.data?.requiredApprovals ?? 1,
        requireSeparationOfDuties: separation ?? policy.data?.requireSeparationOfDuties ?? true,
      })
      policy.refresh()
    }
    catch (e) { setError(e instanceof Error ? e.message : 'Could not update approval policy.') }
  }

  const decide = async (change: ChangeRequest, decision: 'Approved' | 'Rejected') => {
    setError(null)
    try {
      await api.decideChangeRequest(change.id, decision, comments[change.id] ?? '')
      setComments(current => ({ ...current, [change.id]: '' })); changes.refresh()
    } catch (e) { setError(e instanceof Error ? e.message : `Could not ${decision.toLowerCase()} change request.`) }
  }

  const cancel = async (change: ChangeRequest) => {
    setError(null)
    try { await api.cancelChangeRequest(change.id); changes.refresh() }
    catch (e) { setError(e instanceof Error ? e.message : 'Could not cancel change request.') }
  }

  return <>
    <PageHeader eyebrow="Governance" title="Change requests" description="Record planned operational changes and enforce tenant approval thresholds before scheduling work." actions={<button type="button" className="button button-ghost" onClick={refresh}>Refresh</button>} />
    {error ? <ErrorBanner message={error} /> : null}
    {canManage(user) ? <PageSection title="Approval policy" description="Policy is captured when a request is created; changes do not rewrite existing requests.">
      {policy.error ? <ErrorBanner message={policy.error} onRetry={policy.refresh} /> : null}
      {policy.data ? <form className="enrollment-controls" onSubmit={updatePolicy}>
        <label>Required approvals<input type="number" min={1} max={5} value={requiredApprovals ?? policy.data.requiredApprovals} onChange={e => setRequiredApprovals(Number(e.target.value))} /></label>
        <label><input type="checkbox" checked={separation ?? policy.data.requireSeparationOfDuties} onChange={e => setSeparation(e.target.checked)} /> Require a different requester and approver</label>
        <button type="submit" className="button button-ghost">Save policy</button>
      </form> : <Loading label="Loading approval policy" />}
    </PageSection> : null}
    {canOperate(user) ? <PageSection title="Submit a change" description={`New requests require ${policy.data?.requiredApprovals ?? 1} approval(s). Deployment execution remains a separate disabled capability.`}>
      <form className="enrollment-controls" onSubmit={create}>
        <label>Title<input required maxLength={200} value={title} onChange={e => setTitle(e.target.value)} /></label>
        <label>Environment<select value={environment} onChange={e => setEnvironment(e.target.value)}><option>development</option><option>test</option><option>staging</option><option>production</option></select></label>
        <label>Planned start<input required type="datetime-local" value={start} onChange={e => setStart(e.target.value)} /></label>
        <label>Planned end<input required type="datetime-local" value={end} onChange={e => setEnd(e.target.value)} /></label>
        <label>Related incident<select value={incidentId} onChange={e => setIncidentId(e.target.value)}><option value="">None</option>{incidents.data?.filter(i => i.status !== 'Closed').map(i => <option key={i.id} value={i.id}>{i.number} · {i.title}</option>)}</select></label>
        <label>Risk assessment<textarea required maxLength={4000} value={risk} onChange={e => setRisk(e.target.value)} /></label>
        <label>Change plan<textarea required maxLength={8000} value={description} onChange={e => setDescription(e.target.value)} /></label>
        <button type="submit" className="button button-primary">Submit for approval</button>
      </form>
    </PageSection> : null}
    <PageSection title="Change register" description={`${changes.data?.length ?? 0} requests`}>
      {changes.loading && !changes.data ? <Loading label="Loading change requests" /> : null}
      {changes.error ? <ErrorBanner message={changes.error} onRetry={changes.refresh} /> : null}
      {!changes.loading && changes.data?.length === 0 ? <EmptyState title="No change requests" detail="Submitted changes appear here with their approval decisions." /> : null}
      {!!changes.data?.length ? <div className="table-wrap"><table><thead><tr><th>Request</th><th>Window</th><th>Risk</th><th>Status</th><th>Approvals</th><th>Decision</th></tr></thead><tbody>{changes.data.map(change => {
        const self = change.requestedByUserId === user?.id
        const mayDecide = canApprove(user) && !self && change.status === 'PendingApproval'
        const mayCancel = canOperate(user) && change.status === 'PendingApproval' && (self || canManage(user))
        return <tr key={change.id}><td><strong>{change.number} · {change.title}</strong><span className="muted block">{change.targetEnvironment} · requested by {change.requestedByDisplay}</span><span className="muted block">{change.description}</span>{change.relatedIncidentId ? <span className="muted block">Related incident {incidents.data?.find(i => i.id === change.relatedIncidentId)?.number ?? change.relatedIncidentId}</span> : null}</td><td>{formatDate(change.plannedStartAt)}<span className="muted block">to {formatDate(change.plannedEndAt)}</span></td><td>{change.risk}</td><td><StatusBadge value={change.status} /></td><td>{change.approvals.length}/{change.requiredApprovals}<span className="muted block">{change.approvals.map(a => `${a.approverDisplay}: ${a.decision}`).join(' · ') || 'Awaiting review'}</span></td><td>{mayDecide ? <div className="status-stack"><input aria-label={`Decision comment for ${change.number}`} placeholder="Decision comment" value={comments[change.id] ?? ''} onChange={e => setComments(current => ({ ...current, [change.id]: e.target.value }))} /><div className="inline-actions"><button type="button" className="button button-primary" onClick={() => decide(change, 'Approved')}>Approve</button><button type="button" className="button button-ghost" onClick={() => decide(change, 'Rejected')}>Reject</button></div></div> : null}{mayCancel ? <button type="button" className="button button-ghost" onClick={() => cancel(change)}>Cancel request</button> : null}{!mayDecide && !mayCancel ? '—' : null}</td></tr>
      })}</tbody></table></div> : null}
    </PageSection>
  </>
}
