import { useMemo, useState } from 'react'
import { api } from '../api/client'
import { canManage } from '../api/session'
import { useAuth } from '../auth/authContext'
import { EmptyState, ErrorBanner, Loading, PageHeader, PageSection, StatusBadge } from '../components/ui'
import { useLoad } from '../hooks/useLoad'
import { formatDate, relativeTime } from '../utils'

export function AuditPage() {
  const { user } = useAuth()
  const [search, setSearch] = useState('')
  const allowed = canManage(user)
  const audit = useLoad('audit', () => (allowed ? api.audit(200) : Promise.resolve([])))

  const filtered = useMemo(() => {
    const needle = search.trim().toLowerCase()
    if (!needle) return audit.data ?? []
    return (audit.data ?? []).filter(
      (event) =>
        event.action.toLowerCase().includes(needle) ||
        (event.actorDisplay ?? '').toLowerCase().includes(needle) ||
        event.targetType.toLowerCase().includes(needle) ||
        (event.targetId ?? '').toLowerCase().includes(needle),
    )
  }, [audit.data, search])

  if (!allowed) {
    return (
      <>
        <PageHeader eyebrow="Security" title="Audit log" />
        <EmptyState title="Administrator access required" detail="Only tenant administrators can review audit events." />
      </>
    )
  }

  return (
    <>
      <PageHeader
        eyebrow="Security"
        title="Audit log"
        description="Immutable security and administrative events for this tenant."
        actions={
          <button type="button" className="button button-ghost" onClick={audit.refresh}>
            Refresh
          </button>
        }
      />

      <PageSection
        title="Events"
        description={`${filtered.length} of ${audit.data?.length ?? 0} events shown.`}
        actions={
          <input
            type="search"
            placeholder="Search action, actor, or target"
            value={search}
            onChange={(event) => setSearch(event.target.value)}
            aria-label="Search audit events"
          />
        }
      >
        {audit.loading && !audit.data ? <Loading label="Loading audit log" /> : null}
        {audit.error ? <ErrorBanner message={audit.error} onRetry={audit.refresh} /> : null}
        {!audit.loading && filtered.length === 0 ? <EmptyState title="No matching audit events" /> : null}
        {filtered.length > 0 ? (
          <div className="table-wrap">
            <table>
              <thead>
                <tr>
                  <th>Action</th>
                  <th>Actor</th>
                  <th>Target</th>
                  <th>Outcome</th>
                  <th>IP</th>
                  <th>When</th>
                  <th>Details</th>
                </tr>
              </thead>
              <tbody>
                {filtered.map((event) => (
                  <tr key={event.id}>
                    <td>
                      <code>{event.action}</code>
                    </td>
                    <td>
                      {event.actorDisplay ?? '—'}
                      <span className="muted block small">{event.actorType}</span>
                    </td>
                    <td>
                      {event.targetType}
                      {event.targetId ? <span className="muted block small">{event.targetId}</span> : null}
                    </td>
                    <td>
                      <StatusBadge value={event.outcome} />
                    </td>
                    <td>{event.ipAddress ?? '—'}</td>
                    <td title={formatDate(event.createdAt)}>{relativeTime(event.createdAt)}</td>
                    <td>
                      {event.detailsJson ? (
                        <details>
                          <summary>View</summary>
                          <pre>{event.detailsJson}</pre>
                        </details>
                      ) : (
                        '—'
                      )}
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
