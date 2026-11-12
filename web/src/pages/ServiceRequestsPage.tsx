import { useState, type FormEvent } from 'react'
import { api } from '../api/client'
import { canOperate } from '../api/session'
import { useAuth } from '../auth/authContext'
import { TicketCollaborationPanel } from '../components/TicketCollaborationPanel'
import { EmptyState, ErrorBanner, Loading, PageHeader, PageSection, StatusBadge } from '../components/ui'
import { useLoad } from '../hooks/useLoad'
import { formatDate } from '../utils'

const nextStatuses: Record<string, string[]> = {
  New: ['Acknowledged', 'InProgress', 'Resolved'],
  Acknowledged: ['InProgress', 'Resolved'],
  InProgress: ['Resolved'],
  Resolved: ['Closed'],
}

const actionLabels: Record<string, string> = {
  Acknowledged: 'Acknowledge',
  InProgress: 'Start work',
  Resolved: 'Resolve',
  Closed: 'Close',
}

const categories = ['Access', 'Hardware', 'Software', 'Network', 'Other']
const statusFilters = ['all', 'New', 'Acknowledged', 'InProgress', 'Resolved', 'Closed']

export function ServiceRequestsPage() {
  const { user } = useAuth()
  const [status, setStatus] = useState('all')
  const requests = useLoad(`service-requests:${status}`, () =>
    api.serviceRequests(status === 'all' ? undefined : status),
  )
  const [title, setTitle] = useState('')
  const [description, setDescription] = useState('')
  const [category, setCategory] = useState('Access')
  const [error, setError] = useState<string | null>(null)
  const [busyId, setBusyId] = useState<string | null>(null)
  const [selected, setSelected] = useState<string | null>(null)

  const create = async (event: FormEvent) => {
    event.preventDefault()
    setError(null)
    try {
      await api.createServiceRequest({ title, description, category })
      setTitle('')
      setDescription('')
      requests.refresh()
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : 'Could not create service request.')
    }
  }

  const transition = async (id: string, next: string) => {
    setBusyId(id)
    setError(null)
    try {
      await api.transitionServiceRequest(id, next)
      requests.refresh()
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : 'Could not update service request.')
    } finally {
      setBusyId(null)
    }
  }

  return (
    <>
      <PageHeader
        eyebrow="Service desk"
        title="Service requests"
        description="Raise, triage, and resolve non-incident requests for access, hardware, software, and network support."
        actions={
          <button type="button" className="button button-ghost" onClick={requests.refresh}>
            Refresh
          </button>
        }
      />

      {error ? <ErrorBanner message={error} /> : null}

      {canOperate(user) ? (
        <PageSection
          title="Raise a request"
          description="Requests are visible to the whole tenant and follow a simple fulfilment workflow."
        >
          <form className="enrollment-controls" onSubmit={create}>
            <label>
              Title
              <input required maxLength={200} value={title} onChange={(event) => setTitle(event.target.value)} />
            </label>
            <label>
              Category
              <select value={category} onChange={(event) => setCategory(event.target.value)}>
                {categories.map((item) => (
                  <option key={item} value={item}>
                    {item}
                  </option>
                ))}
              </select>
            </label>
            <label>
              Description
              <textarea
                required
                maxLength={8000}
                value={description}
                onChange={(event) => setDescription(event.target.value)}
              />
            </label>
            <button type="submit" className="button button-primary">
              Create request
            </button>
          </form>
        </PageSection>
      ) : null}

      <PageSection
        title="Request queue"
        description={`${requests.data?.length ?? 0} requests in the selected view.`}
        actions={
          <select
            value={status}
            onChange={(event) => setStatus(event.target.value)}
            aria-label="Service request status filter"
          >
            {statusFilters.map((value) => (
              <option key={value} value={value}>
                {value === 'all' ? 'All statuses' : value === 'InProgress' ? 'In progress' : value}
              </option>
            ))}
          </select>
        }
      >
        {requests.loading && !requests.data ? <Loading label="Loading service requests" /> : null}
        {requests.error ? <ErrorBanner message={requests.error} onRetry={requests.refresh} /> : null}
        {!requests.loading && (requests.data?.length ?? 0) === 0 ? (
          <EmptyState
            title="No service requests"
            detail="Raise a request to start the fulfilment workflow for this tenant."
          />
        ) : null}
        {(requests.data?.length ?? 0) > 0 ? (
          <div className="table-wrap">
            <table>
              <thead>
                <tr>
                  <th>Request</th>
                  <th>Category</th>
                  <th>Status</th>
                  <th>Requested by</th>
                  <th>Opened</th>
                  <th>Actions</th>
                </tr>
              </thead>
              <tbody>
                {requests.data?.map((request) => (
                  <tr key={request.id}>
                    <td>
                      <strong>{request.number}</strong>{' '}
                      <button type="button" className="button button-ghost" onClick={() => setSelected(request.id)}>
                        Details
                      </button>
                      <span className="muted block">{request.title}</span>
                      <span className="muted block">{request.description}</span>
                    </td>
                    <td>{request.category}</td>
                    <td>
                      <StatusBadge value={request.status} label={request.status === 'InProgress' ? 'In progress' : request.status} />
                    </td>
                    <td>{request.requestedByDisplay}</td>
                    <td>{formatDate(request.openedAt)}</td>
                    <td>
                      {canOperate(user) && (nextStatuses[request.status] ?? []).length > 0 ? (
                        <div className="inline-actions">
                          {(nextStatuses[request.status] ?? []).map((next) => (
                            <button
                              key={next}
                              type="button"
                              className="button button-ghost"
                              onClick={() => transition(request.id, next)}
                              disabled={busyId === request.id}
                            >
                              {actionLabels[next] ?? next}
                            </button>
                          ))}
                        </div>
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
      {selected ? (
        <TicketCollaborationPanel ticketId={selected} onClose={() => setSelected(null)} onChanged={requests.refresh} />
      ) : null}
    </>
  )
}
