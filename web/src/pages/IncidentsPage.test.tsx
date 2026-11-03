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

    fireEvent.click(await screen.findByRole('button', { name: 'Acknowledged' }))
    await waitFor(() => expect(transition).toHaveBeenCalledWith('i1', 'Acknowledged'))
  })

  it('hides incident creation and transitions from viewers', async () => {
    vi.spyOn(api, 'incidents').mockResolvedValue([incident])
    vi.spyOn(api, 'alerts').mockResolvedValue([])
    renderPage(viewer)

    expect(await screen.findByText('INC-1')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Create incident' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Acknowledged' })).not.toBeInTheDocument()
  })
})
