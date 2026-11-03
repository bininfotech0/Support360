import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { AuthContext } from '../auth/authContext'
import { api } from '../api/client'
import { AgentsPage } from './AgentsPage'

vi.mock('../hooks/useRealtime', () => ({ useRealtime: () => false }))

const admin = { id: 'u1', tenantId: 't1', tenantName: 'Tenant', isDemoTenant: false, email: 'admin@test', displayName: 'Admin', role: 'TenantAdmin' as const }

function renderPage() {
  return render(<MemoryRouter><AuthContext.Provider value={{ session: null, user: admin, login: vi.fn(), logout: vi.fn() }}><AgentsPage /></AuthContext.Provider></MemoryRouter>)
}

describe('AgentsPage', () => {
  afterEach(() => vi.restoreAllMocks())

  it('leaves deployment permission off unless the tenant admin selects it', async () => {
    vi.spyOn(api, 'agents').mockResolvedValue([])
    vi.spyOn(api, 'enrollments').mockResolvedValue([])
    const create = vi.spyOn(api, 'createEnrollment').mockResolvedValue({
      enrollmentId: 'e1', token: 'one-time-token', tokenPrefix: 'one-time', targetPlatform: { platform: 'Linux' },
      expiresAt: '2026-10-30T12:00:00Z', enrollCommand: 'support360-agent enroll --enable-deployments',
      allowDeploymentExecution: true,
    })
    renderPage()

    const checkbox = await screen.findByRole('checkbox', { name: /grant controlled deployment permission/i })
    expect(checkbox).not.toBeChecked()
    fireEvent.click(checkbox)
    fireEvent.click(screen.getByRole('button', { name: 'Create enrollment token' }))

    await waitFor(() => expect(create).toHaveBeenCalledWith('Linux', true))
    expect(await screen.findByText(/granted by administrator; local opt-in is also required/i)).toBeInTheDocument()
  })
})
