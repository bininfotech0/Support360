import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { api } from '../api/client'
import { AuthContext } from '../auth/authContext'
import { ChangeRequestsPage } from './ChangeRequestsPage'

const admin = { id: 'u1', tenantId: 't1', tenantName: 'Tenant', isDemoTenant: false, email: 'admin@test', displayName: 'Admin', role: 'TenantAdmin' as const }
const change = {
  id: 'c1', number: 'CHG-1', title: 'Restart API', description: 'Rolling restart', risk: 'Low', targetEnvironment: 'production',
  plannedStartAt: '2026-10-30T10:00:00Z', plannedEndAt: '2026-10-30T11:00:00Z', requiredApprovals: 1, status: 'PendingApproval',
  requestedByUserId: 'u2', requestedByDisplay: 'Requester', relatedIncidentId: null, approvals: []
}

function renderPage() {
  return render(<AuthContext.Provider value={{ session: null, user: admin, login: vi.fn(), logout: vi.fn() }}><ChangeRequestsPage /></AuthContext.Provider>)
}

describe('ChangeRequestsPage', () => {
  afterEach(() => vi.restoreAllMocks())

  it('submits a change request with a planned window and risk assessment', async () => {
    vi.spyOn(api, 'changeRequests').mockResolvedValue([])
    vi.spyOn(api, 'incidents').mockResolvedValue([])
    vi.spyOn(api, 'changeApprovalPolicy').mockResolvedValue({ requiredApprovals: 2, requireSeparationOfDuties: true })
    const create = vi.spyOn(api, 'createChangeRequest').mockResolvedValue(change)
    renderPage()

    fireEvent.change(await screen.findByLabelText('Title'), { target: { value: 'Restart API' } })
    fireEvent.change(screen.getByLabelText('Risk assessment'), { target: { value: 'Low' } })
    fireEvent.change(screen.getByLabelText('Change plan'), { target: { value: 'Rolling restart' } })
    fireEvent.click(screen.getByRole('button', { name: 'Submit for approval' }))

    await waitFor(() => expect(create).toHaveBeenCalledWith(expect.objectContaining({
      title: 'Restart API', description: 'Rolling restart', risk: 'Low', targetEnvironment: 'production', relatedIncidentId: null,
    })))
  })

  it('records an approval decision with a comment', async () => {
    vi.spyOn(api, 'changeRequests').mockResolvedValue([change])
    vi.spyOn(api, 'incidents').mockResolvedValue([])
    vi.spyOn(api, 'changeApprovalPolicy').mockResolvedValue({ requiredApprovals: 1, requireSeparationOfDuties: true })
    const decide = vi.spyOn(api, 'decideChangeRequest').mockResolvedValue({ ...change, status: 'Approved' })
    renderPage()

    fireEvent.change(await screen.findByLabelText('Decision comment for CHG-1'), { target: { value: 'Looks good' } })
    fireEvent.click(screen.getByRole('button', { name: 'Approve' }))
    await waitFor(() => expect(decide).toHaveBeenCalledWith('c1', 'Approved', 'Looks good'))
  })

  it('prevents requesters from approving their own change', async () => {
    vi.spyOn(api, 'changeRequests').mockResolvedValue([{ ...change, requestedByUserId: admin.id }])
    vi.spyOn(api, 'incidents').mockResolvedValue([])
    vi.spyOn(api, 'changeApprovalPolicy').mockResolvedValue({ requiredApprovals: 1, requireSeparationOfDuties: true })
    renderPage()

    expect(await screen.findByText('CHG-1 · Restart API')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Approve' })).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Cancel request' })).toBeInTheDocument()
  })

  it('updates the tenant approval policy', async () => {
    vi.spyOn(api, 'changeRequests').mockResolvedValue([])
    vi.spyOn(api, 'incidents').mockResolvedValue([])
    vi.spyOn(api, 'changeApprovalPolicy').mockResolvedValue({ requiredApprovals: 2, requireSeparationOfDuties: true })
    const update = vi.spyOn(api, 'updateChangeApprovalPolicy').mockResolvedValue({ requiredApprovals: 3, requireSeparationOfDuties: true })
    renderPage()

    fireEvent.change(await screen.findByLabelText('Required approvals'), { target: { value: '3' } })
    fireEvent.click(screen.getByRole('button', { name: 'Save policy' }))
    await waitFor(() => expect(update).toHaveBeenCalledWith({ requiredApprovals: 3, requireSeparationOfDuties: true }))
  })
})
