import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { api } from '../api/client'
import { AuthContext } from '../auth/authContext'
import { ServiceRequestsPage } from './ServiceRequestsPage'

const admin = { id: 'u1', tenantId: 't1', tenantName: 'Tenant', isDemoTenant: false, email: 'admin@test', displayName: 'Admin', role: 'TenantAdmin' as const }
const viewer = { ...admin, id: 'u2', email: 'viewer@test', displayName: 'Viewer', role: 'Viewer' as const }
const request = {
  id: 'sr1', number: 'SR-1', title: 'VPN access', description: 'Need access to the VPN', category: 'Access', status: 'New',
  requestedByUserId: 'u1', requestedByDisplay: 'Admin', assignedToUserId: null, openedAt: '2026-10-30T10:00:00Z',
  acknowledgedAt: null, resolvedAt: null, closedAt: null,
}

function renderPage(user: typeof admin | typeof viewer = admin) {
  return render(<AuthContext.Provider value={{ session: null, user, login: vi.fn(), logout: vi.fn() }}><ServiceRequestsPage /></AuthContext.Provider>)
}

describe('ServiceRequestsPage', () => {
  afterEach(() => vi.restoreAllMocks())

  it('raises a service request', async () => {
    vi.spyOn(api, 'serviceRequests').mockResolvedValue([])
    const create = vi.spyOn(api, 'createServiceRequest').mockResolvedValue(request)
    renderPage()

    fireEvent.change(await screen.findByLabelText('Title'), { target: { value: 'VPN access' } })
    fireEvent.change(screen.getByLabelText('Category'), { target: { value: 'Access' } })
    fireEvent.change(screen.getByLabelText('Description'), { target: { value: 'Need access to the VPN' } })
    fireEvent.click(screen.getByRole('button', { name: 'Create request' }))

    await waitFor(() => expect(create).toHaveBeenCalledWith({ title: 'VPN access', description: 'Need access to the VPN', category: 'Access' }))
  })

  it('filters requests by status', async () => {
    const list = vi.spyOn(api, 'serviceRequests').mockResolvedValue([request])
    renderPage()

    expect(await screen.findByText('SR-1')).toBeInTheDocument()
    fireEvent.change(screen.getByLabelText('Service request status filter'), { target: { value: 'Resolved' } })

    await waitFor(() => expect(list).toHaveBeenCalledWith('Resolved'))
  })

  it('advances a request to the next status', async () => {
    vi.spyOn(api, 'serviceRequests').mockResolvedValue([request])
    const transition = vi.spyOn(api, 'transitionServiceRequest').mockResolvedValue({ ...request, status: 'Acknowledged' })
    renderPage()

    fireEvent.click(await screen.findByRole('button', { name: 'Acknowledge' }))
    await waitFor(() => expect(transition).toHaveBeenCalledWith('sr1', 'Acknowledged'))
  })

  it('hides creation and transitions from viewers', async () => {
    vi.spyOn(api, 'serviceRequests').mockResolvedValue([request])
    renderPage(viewer)

    expect(await screen.findByText('SR-1')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Create request' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Acknowledge' })).not.toBeInTheDocument()
  })
})
