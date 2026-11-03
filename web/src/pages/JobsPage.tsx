import { useState, type FormEvent } from 'react'
import { Link } from 'react-router-dom'
import { api } from '../api/client'
import { EmptyState, ErrorBanner, Loading, PageHeader, PageSection, StatusBadge } from '../components/ui'
import { useAuth } from '../auth/authContext'
import { canManage } from '../api/session'
import { useLoad } from '../hooks/useLoad'
import { useRealtime } from '../hooks/useRealtime'
import { formatDate, formatDuration, relativeTime } from '../utils'

export function JobsPage() {
  const { user } = useAuth()
  const jobs = useLoad('batch-jobs', () => api.batchJobs())
  const agents = useLoad('batch-job-agents', () => api.agents())
  const connected = useRealtime(jobs.refresh)
  const [agentId, setAgentId] = useState('')
  const [jobKey, setJobKey] = useState('')
  const [name, setName] = useState('')
  const [scheduler, setScheduler] = useState('')
  const [expectedSchedule, setExpectedSchedule] = useState('')
  const [editing, setEditing] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)

  const save = async (event: FormEvent) => {
    event.preventDefault(); setError(null)
    try {
      if (editing) {
        await api.updateBatchJob(editing, { name, scheduler: scheduler || null, expectedSchedule: expectedSchedule || null })
      } else {
        await api.configureBatchJob({ agentId, jobKey, name, scheduler: scheduler || null, expectedSchedule: expectedSchedule || null })
      }
      setEditing(null); setJobKey(''); setName(''); setScheduler(''); setExpectedSchedule(''); jobs.refresh()
    } catch (e) { setError(e instanceof Error ? e.message : 'Could not save batch job configuration.') }
  }

  const edit = (job: NonNullable<typeof jobs.data>[number]) => {
    setEditing(job.id); setAgentId(job.agentId); setJobKey(job.jobKey); setName(job.name)
    setScheduler(job.scheduler ?? ''); setExpectedSchedule(job.expectedSchedule ?? '')
  }

  return (
    <>
      <PageHeader
        eyebrow="Automation"
        title="Batch jobs"
        description="Scheduled job outcomes reported by enrolled agents. No job is executed from this console."
        actions={
          <span className={`live-status ${connected ? 'is-live' : ''}`}>
            <span className="live-dot" aria-hidden="true" />
            {connected ? 'Live' : 'Reconnecting'}
          </span>
        }
      />

      {error ? <ErrorBanner message={error} /> : null}
      {canManage(user) ? <PageSection title={editing ? 'Edit job configuration' : 'Configure a batch job'} description="Register the job key and expected schedule for an active agent. This records expectations; it does not execute or schedule the job.">
        <form className="enrollment-controls" onSubmit={save}>
          {!editing ? <label>Agent<select required value={agentId} onChange={e => setAgentId(e.target.value)}><option value="">Select agent</option>{agents.data?.filter(agent => agent.status === 'Active').map(agent => <option key={agent.id} value={agent.id}>{agent.hostname}</option>)}</select></label> : null}
          {!editing ? <label>Job key<input required maxLength={200} value={jobKey} onChange={e => setJobKey(e.target.value)} /></label> : <label>Job key<input disabled value={jobKey} /></label>}
          <label>Name<input required maxLength={255} value={name} onChange={e => setName(e.target.value)} /></label>
          <label>Source scheduler<input maxLength={255} value={scheduler} onChange={e => setScheduler(e.target.value)} placeholder="Control-M, cron, SQL Agent…" /></label>
          <label>Expected schedule<input maxLength={255} value={expectedSchedule} onChange={e => setExpectedSchedule(e.target.value)} placeholder="Weekdays at 02:00 UTC" /></label>
          <button type="submit" className="button button-primary" disabled={!editing && !agentId}>{editing ? 'Save job' : 'Configure job'}</button>
          {editing ? <button type="button" className="button button-ghost" onClick={() => { setEditing(null); setName(''); setScheduler(''); setExpectedSchedule('') }}>Cancel</button> : null}
        </form>
      </PageSection> : null}

      <PageSection title="Tracked jobs" description={`${jobs.data?.length ?? 0} job keys configured or reported by agents for this tenant.`}>
        {jobs.loading && !jobs.data ? <Loading label="Loading batch jobs" /> : null}
        {jobs.error ? <ErrorBanner message={jobs.error} onRetry={jobs.refresh} /> : null}
        {!jobs.loading && (jobs.data?.length ?? 0) === 0 ? (
          <EmptyState
            title="No batch jobs reported"
            detail="Agents post job events through the authenticated job-events endpoint."
          />
        ) : null}

        <div className="stack">
          {jobs.data?.map((job) => (
            <article key={job.id} className="job-card">
              <div className="job-heading">
                <div>
                  <div className="row-title">
                    <strong>{job.name}</strong>
                    <code>{job.jobKey}</code>
                    <StatusBadge value={job.lastStatus} />
                  </div>
                  <p className="row-meta">
                    <Link className="text-link" to={`/agents/${job.agentId}`}>
                      {job.hostname}
                    </Link>
                    {job.scheduler ? ` · ${job.scheduler}` : ''}
                    {job.expectedSchedule ? ` · ${job.expectedSchedule}` : ''}
                  </p>
                </div>
                <div className="job-summary">
                  <span>Last run {relativeTime(job.lastRunAt)}</span>
                  <span>Exit {job.lastExitCode ?? '—'}</span>
                  {canManage(user) ? <button type="button" className="button button-ghost" onClick={() => edit(job)}>Edit configuration</button> : null}
                </div>
              </div>

              {job.recentExecutions.length === 0 ? (
                <p className="muted small">No executions recorded.</p>
              ) : (
                <details>
                  <summary>Recent executions ({job.recentExecutions.length})</summary>
                  <div className="table-wrap">
                    <table>
                      <thead>
                        <tr>
                          <th>Started</th>
                          <th>Status</th>
                          <th>Exit</th>
                          <th>Duration</th>
                          <th>Failure</th>
                        </tr>
                      </thead>
                      <tbody>
                        {job.recentExecutions.map((execution) => (
                          <tr key={execution.id}>
                            <td title={formatDate(execution.startedAt)}>{relativeTime(execution.startedAt)}</td>
                            <td>
                              <StatusBadge value={execution.status} />
                            </td>
                            <td>{execution.exitCode ?? '—'}</td>
                            <td>{execution.durationSeconds === null ? '—' : formatDuration(execution.durationSeconds)}</td>
                            <td>{execution.failureReason ?? '—'}</td>
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  </div>
                </details>
              )}
            </article>
          ))}
        </div>
      </PageSection>
    </>
  )
}
