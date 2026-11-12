import { useState, type FormEvent } from 'react'
import { api } from '../api/client'
import { canApprove, canOperate } from '../api/session'
import type { Ticket, TicketAttachment } from '../api/types'
import { useAuth } from '../auth/authContext'
import { ErrorBanner, Loading, PageSection, StatusBadge } from './ui'
import { useLoad } from '../hooks/useLoad'
import { formatDate } from '../utils'

function totalMinutes(ticket: Ticket | null): number {
  return ticket?.worklogs.reduce((sum, worklog) => sum + worklog.minutes, 0) ?? 0
}

function formatBytes(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`
}

export function TicketCollaborationPanel({
  ticketId,
  onClose,
  onChanged,
}: {
  ticketId: string
  onClose: () => void
  onChanged?: () => void
}) {
  const { user } = useAuth()
  const ticket = useLoad(`ticket:${ticketId}`, () => api.ticket(ticketId))
  const [comment, setComment] = useState('')
  const [internal, setInternal] = useState(false)
  const [minutes, setMinutes] = useState('30')
  const [note, setNote] = useState('')
  const [dueAt, setDueAt] = useState('')
  const [file, setFile] = useState<File | null>(null)
  const [fileKey, setFileKey] = useState(0)
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  const apply = async (action: () => Promise<Ticket>, reset?: () => void) => {
    setBusy(true)
    setError(null)
    try {
      await action()
      reset?.()
      ticket.refresh()
      onChanged?.()
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : 'Could not update the ticket.')
    } finally {
      setBusy(false)
    }
  }

  const addComment = async (event: FormEvent) => {
    event.preventDefault()
    await apply(() => api.addTicketComment(ticketId, comment, internal), () => {
      setComment('')
      setInternal(false)
    })
  }

  const addWorklog = async (event: FormEvent) => {
    event.preventDefault()
    await apply(() => api.addTicketWorklog(ticketId, Number(minutes), note || null), () => {
      setNote('')
      setMinutes('30')
    })
  }

  const setDueDate = async (event: FormEvent) => {
    event.preventDefault()
    if (!dueAt) return
    await apply(() => api.setTicketDueDate(ticketId, new Date(dueAt).toISOString()), () => setDueAt(''))
  }

  const clearDueDate = async () => {
    await apply(() => api.setTicketDueDate(ticketId, null))
  }

  const uploadAttachment = async (event: FormEvent) => {
    event.preventDefault()
    if (!file) return
    await apply(() => api.uploadTicketAttachment(ticketId, file), () => {
      setFile(null)
      setFileKey((value) => value + 1)
    })
  }

  const downloadAttachment = async (attachment: TicketAttachment) => {
    setError(null)
    try {
      const blob = await api.downloadTicketAttachment(ticketId, attachment.id)
      const url = URL.createObjectURL(blob)
      const anchor = document.createElement('a')
      anchor.href = url
      anchor.download = attachment.fileName
      anchor.click()
      URL.revokeObjectURL(url)
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : 'Could not download the attachment.')
    }
  }

  const removeAttachment = async (attachment: TicketAttachment) => {
    await apply(() => api.deleteTicketAttachment(ticketId, attachment.id))
  }

  const watching = ticket.data?.watchers.some((watcher) => watcher.userId === user?.id) ?? false

  return (
    <PageSection
      title={ticket.data ? `${ticket.data.number} collaboration` : 'Ticket collaboration'}
      description={ticket.data?.title}
      actions={
        <div className="inline-actions">
          {ticket.data ? (
            <button
              type="button"
              className="button button-ghost"
              onClick={() => apply(() => api.setTicketWatching(ticketId, !watching))}
              disabled={busy}
            >
              {watching ? 'Stop watching' : 'Watch'}
            </button>
          ) : null}
          <button type="button" className="button button-ghost" onClick={onClose}>
            Close
          </button>
        </div>
      }
    >
      {error ? <ErrorBanner message={error} /> : null}
      {ticket.loading && !ticket.data ? <Loading label="Loading ticket" /> : null}
      {ticket.error ? <ErrorBanner message={ticket.error} onRetry={ticket.refresh} /> : null}
      {ticket.data ? (
        <>
          <div className="inline-actions">
            <StatusBadge value={ticket.data.status} />
            <StatusBadge value={ticket.data.priority} />
            <span className="muted">Opened {formatDate(ticket.data.openedAt)}</span>
            <span className="muted">{totalMinutes(ticket.data)} minutes logged</span>
            <span className="muted">{ticket.data.dueAt ? `Due ${formatDate(ticket.data.dueAt)}` : 'No due date'}</span>
          </div>
          {canOperate(user) ? (
            <form className="enrollment-controls" onSubmit={setDueDate}>
              <label>
                Due date
                <input type="datetime-local" value={dueAt} onChange={(event) => setDueAt(event.target.value)} />
              </label>
              <button type="submit" className="button button-primary" disabled={busy || !dueAt}>
                Set due date
              </button>
              {ticket.data.dueAt ? (
                <button type="button" className="button button-ghost" onClick={clearDueDate} disabled={busy}>
                  Clear due date
                </button>
              ) : null}
            </form>
          ) : null}

          <h3>Comments</h3>
          {ticket.data.comments.length ? (
            <div className="status-stack">
              {ticket.data.comments.map((item) => (
                <div key={item.id} className="muted block">
                  <strong>{item.authorDisplay ?? 'Unknown'}</strong>
                  {item.isInternal ? ' · internal note' : ''} · {formatDate(item.createdAt)}
                  <span className="block">{item.body}</span>
                </div>
              ))}
            </div>
          ) : (
            <p className="muted">No comments yet.</p>
          )}
          <form className="enrollment-controls" onSubmit={addComment}>
            <label>
              Comment
              <textarea required maxLength={8000} value={comment} onChange={(event) => setComment(event.target.value)} />
            </label>
            {canOperate(user) || canApprove(user) ? (
              <label>
                <input type="checkbox" checked={internal} onChange={(event) => setInternal(event.target.checked)} /> Internal note
              </label>
            ) : null}
            <button type="submit" className="button button-primary" disabled={busy}>
              Add comment
            </button>
          </form>

          <h3>Worklogs</h3>
          {ticket.data.worklogs.length ? (
            <div className="status-stack">
              {ticket.data.worklogs.map((worklog) => (
                <div key={worklog.id} className="muted block">
                  <strong>{worklog.authorDisplay ?? 'Unknown'}</strong> · {worklog.minutes} minutes · {formatDate(worklog.workedAt)}
                  {worklog.note ? <span className="block">{worklog.note}</span> : null}
                </div>
              ))}
            </div>
          ) : (
            <p className="muted">No worklogs yet.</p>
          )}
          {canOperate(user) ? (
            <form className="enrollment-controls" onSubmit={addWorklog}>
              <label>
                Minutes
                <input type="number" min={1} max={1440} required value={minutes} onChange={(event) => setMinutes(event.target.value)} />
              </label>
              <label>
                Note
                <input maxLength={2000} value={note} onChange={(event) => setNote(event.target.value)} />
              </label>
              <button type="submit" className="button button-primary" disabled={busy}>
                Log work
              </button>
            </form>
          ) : null}

          <h3>Attachments</h3>
          {ticket.data.attachments.length ? (
            <div className="status-stack">
              {ticket.data.attachments.map((attachment) => (
                <div key={attachment.id} className="muted block">
                  <strong>{attachment.fileName}</strong> · {formatBytes(attachment.sizeBytes)} ·{' '}
                  {attachment.uploadedByDisplay} · {formatDate(attachment.uploadedAt)}{' '}
                  <button type="button" className="button button-ghost" onClick={() => downloadAttachment(attachment)}>
                    Download
                  </button>
                  {canOperate(user) ? (
                    <button type="button" className="button button-ghost" onClick={() => removeAttachment(attachment)} disabled={busy}>
                      Remove
                    </button>
                  ) : null}
                </div>
              ))}
            </div>
          ) : (
            <p className="muted">No attachments.</p>
          )}
          {canOperate(user) ? (
            <form className="enrollment-controls" onSubmit={uploadAttachment}>
              <label>
                File
                <input key={fileKey} type="file" onChange={(event) => setFile(event.target.files?.[0] ?? null)} />
              </label>
              <button type="submit" className="button button-primary" disabled={busy || !file}>
                Upload attachment
              </button>
            </form>
          ) : null}

          <h3>Watchers</h3>
          <p className="muted">
            {ticket.data.watchers.length ? ticket.data.watchers.map((watcher) => watcher.display).join(', ') : 'No watchers.'}
          </p>

          <h3>Activity</h3>
          {ticket.data.activities.length ? (
            <div className="status-stack">
              {ticket.data.activities.map((activity, index) => (
                <div key={`${activity.occurredAt}-${index}`} className="muted block">
                  <strong>{activity.kind.replaceAll('_', ' ')}</strong>
                  {activity.actorDisplay ? ` · ${activity.actorDisplay}` : ''} · {formatDate(activity.occurredAt)}
                  {activity.detail ? <span className="block">{activity.detail}</span> : null}
                </div>
              ))}
            </div>
          ) : (
            <p className="muted">No activity recorded.</p>
          )}
        </>
      ) : null}
    </PageSection>
  )
}
