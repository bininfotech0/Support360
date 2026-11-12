import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { api } from '../api/client'
import { AuthContext } from '../auth/authContext'
import { IncidentsPage } from './IncidentsPage'

const admin = { id: 'u1', tenantId: 't1', tenantName: 'Tenant', isDemoTenant: false, email: 'admin@test', displayName: 'Admin', role: 'TenantAdmin' as const }
const viewer = { ...admin, id: 'u2', email: 'viewer@test', displayName: 'Viewer', role: 'Viewer' as const }
const incident = {
  id: 'i1', number: 'INC-1', title: 'API unavailable', description: 'Health checks failing', severity: 'High', status: 'New',
  createdByUserId: 'u1', assignedToUserId: null, relatedBatchJobExecutionId: null, openedAt: '2026-10-30T10:00:00Z',
  acknowledgedAt: null, resolvedAt: null, closedAt: null, alerts: [], relatedChangeRequestIds: []
}
const detailTicket = {
  id: 'i1', number: 'INC-1', type: 'Incident', title: 'API unavailable', description: 'Health checks failing', category: null,
  status: 'New', priority: 'High', impact: 'Medium', urgency: 'Medium', requestedByUserId: 'u1', requestedByDisplay: 'Admin',
  assignedToUserId: null, relatedBatchJobExecutionId: null, risk: null, targetEnvironment: null,
  plannedStartAt: null, plannedEndAt: null, requiredApprovals: 1, openedAt: '2026-10-30T10:00:00Z',
  acknowledgedAt: null, resolvedAt: null, closedAt: null, alerts: [], approvals: [], relatedTickets: [],
  activities: [], worklogs: [], watchers: [], attachments: [], dueAt: null,
  comments: [{ id: 'c1', body: 'Existing comment', isInternal: false, authorDisplay: 'Admin', createdAt: '2026-10-30T11:00:00Z' }],
}

function renderPage(user: typeof admin | typeof viewer = admin) {
  return render(<AuthContext.Provider value={{ session: null, user, login: vi.fn(), logout: vi.fn() }}><IncidentsPage /></AuthContext.Provider>)
}

describe('IncidentsPage', () => {
  afterEach(() => vi.restoreAllMocks())

  it('creates an incident linked to an active alert', async () => {
    vi.spyOn(api, 'incidents').mockResolvedValue([])
    vi.spyOn(api, 'alerts').mockResolvedValue([{ id: 'al1', hostname: 'host1', ruleName: 'CPU high' } as never])
    const create = vi.spyOn(api, 'createIncident').mockResolvedValue(incident)
    renderPage()

    fireEvent.change(await screen.findByLabelText('Title'), { target: { value: 'API unavailable' } })
    fireEvent.change(screen.getByLabelText('Severity'), { target: { value: 'High' } })
    fireEvent.change(screen.getByLabelText('Link active alert'), { target: { value: 'al1' } })
    fireEvent.change(screen.getByLabelText('Description'), { target: { value: 'Health checks failing' } })
    fireEvent.click(screen.getByRole('button', { name: 'Create incident' }))

    await waitFor(() => expect(create).toHaveBeenCalledWith({
      title: 'API unavailable', description: 'Health checks failing', severity: 'High',
      alertIds: ['al1'], relatedBatchJobExecutionId: null,
    }))
  })

  it('transitions an incident through the next valid status', async () => {
    vi.spyOn(api, 'incidents').mockResolvedValue([incident])
    vi.spyOn(api, 'alerts').mockResolvedValue([])
    const transition = vi.spyOn(api, 'transitionIncident').mockResolvedValue({ ...incident, status: 'Acknowledged' })
    renderPage()

    fireEvent.click(await screen.findByRole('button', { name: 'Acknowledge' }))
    await waitFor(() => expect(transition).toHaveBeenCalledWith('i1', 'Acknowledged'))
  })

  it('opens collaboration and adds a comment', async () => {
    vi.spyOn(api, 'incidents').mockResolvedValue([incident])
    vi.spyOn(api, 'alerts').mockResolvedValue([])
    const detail = vi.spyOn(api, 'ticket').mockResolvedValue(detailTicket)
    const addComment = vi.spyOn(api, 'addTicketComment').mockResolvedValue(detailTicket)
    renderPage()

    fireEvent.click(await screen.findByRole('button', { name: 'Details' }))
    expect(await screen.findByText('Existing comment')).toBeInTheDocument()
    fireEvent.change(screen.getByLabelText('Comment'), { target: { value: 'Investigating' } })
    fireEvent.click(screen.getByRole('button', { name: 'Add comment' }))

    await waitFor(() => expect(addComment).toHaveBeenCalledWith('i1', 'Investigating', false))
    expect(detail).toHaveBeenCalledWith('i1')
  })

  it('uploads an attachment from the collaboration panel', async () => {
    vi.spyOn(api, 'incidents').mockResolvedValue([incident])
    vi.spyOn(api, 'alerts').mockResolvedValue([])
    vi.spyOn(api, 'ticket').mockResolvedValue(detailTicket)
    const upload = vi.spyOn(api, 'uploadTicketAttachment').mockResolvedValue(detailTicket)
    renderPage()

    fireEvent.click(await screen.findByRole('button', { name: 'Details' }))
    const file = new File(['evidence'], 'evidence.txt', { type: 'text/plain' })
    fireEvent.change(await screen.findByLabelText('File'), { target: { files: [file] } })
    fireEvent.click(screen.getByRole('button', { name: 'Upload attachment' }))

    await waitFor(() => expect(upload).toHaveBeenCalledWith('i1', file))
  })

  it('sets and clears a due date from the collaboration panel', async () => {
    const dueTicket = { ...detailTicket, dueAt: '2026-11-20T10:00:00.000Z' }
    vi.spyOn(api, 'incidents').mockResolvedValue([incident])
    vi.spyOn(api, 'alerts').mockResolvedValue([])
    vi.spyOn(api, 'ticket').mockResolvedValue(dueTicket)
    const setDueDate = vi.spyOn(api, 'setTicketDueDate')
      .mockResolvedValueOnce(dueTicket)
      .mockResolvedValueOnce(detailTicket)
    renderPage()

    fireEvent.click(await screen.findByRole('button', { name: 'Details' }))
    fireEvent.change(await screen.findByLabelText('Due date'), { target: { value: '2026-11-20T10:00' } })
    fireEvent.click(screen.getByRole('button', { name: 'Set due date' }))
    await waitFor(() => expect(setDueDate).toHaveBeenCalledWith('i1', new Date('2026-11-20T10:00').toISOString()))

    fireEvent.click(await screen.findByRole('button', { name: 'Clear due date' }))
    await waitFor(() => expect(setDueDate).toHaveBeenCalledWith('i1', null))
  })

  it('hides incident creation and transitions from viewers', async () => {
    vi.spyOn(api, 'incidents').mockResolvedValue([incident])
    vi.spyOn(api, 'alerts').mockResolvedValue([])
    renderPage(viewer)

    expect(await screen.findByText('INC-1')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Create incident' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Acknowledge' })).not.toBeInTheDocument()
  })
})
