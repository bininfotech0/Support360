import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { AuthContext } from '../auth/authContext'
import { api } from '../api/client'
import { describe, expect, it, vi, afterEach } from 'vitest'
import { ApplicationsPage } from './ApplicationsPage'

const admin = { id: 'u1', tenantId: 't1', tenantName: 'Tenant', isDemoTenant: false, email: 'admin@test', displayName: 'Admin', role: 'TenantAdmin' as const }
const sample = { id: 'c1', agentId: 'a1', hostname: 'host1', name: 'Web health', type: 'Http', target: 'https://example.test/health', intervalSeconds: 60, enabled: true, lastExecutedAt: '2026-10-30T10:00:00Z', lastDurationMilliseconds: 2, lastStatus: 'Healthy', lastErrorCategory: null }

function renderPage() {
  return render(<AuthContext.Provider value={{ session: null, user: admin, login: vi.fn(), logout: vi.fn() }}><ApplicationsPage /></AuthContext.Provider>)
}

describe('ApplicationsPage', () => {
  afterEach(() => vi.restoreAllMocks())

  it('creates a configured endpoint check', async () => {
    vi.spyOn(api, 'applicationChecks').mockResolvedValue([])
    vi.spyOn(api, 'agents').mockResolvedValue([{ id: 'a1', hostname: 'host1', status: 'Active' } as never])
    const create = vi.spyOn(api, 'createApplicationCheck').mockResolvedValue(sample)
    renderPage()
    fireEvent.change(await screen.findByLabelText('Name'), { target: { value: 'Web health' } })
    fireEvent.change(screen.getByLabelText('Agent'), { target: { value: 'a1' } })
    fireEvent.change(screen.getByLabelText('Target'), { target: { value: 'https://example.test/health' } })
    fireEvent.click(screen.getByRole('button', { name: 'Create check' }))
    await waitFor(() => expect(create).toHaveBeenCalledWith({ agentId: 'a1', name: 'Web health', type: 'Http', target: 'https://example.test/health', intervalSeconds: 60, enabled: true }))
  })

  it('renders check history returned by the API', async () => {
    vi.spyOn(api, 'applicationChecks').mockResolvedValue([sample])
    vi.spyOn(api, 'agents').mockResolvedValue([])
    vi.spyOn(api, 'applicationCheckHistory').mockResolvedValue([{ eventId: 'e1', status: 'Unhealthy', startedAt: '2026-10-30T10:00:00Z', finishedAt: '2026-10-30T10:00:00Z', durationMilliseconds: 12, errorCategory: 'HttpStatus', errorDetail: '503 Service Unavailable' }])
    renderPage()
    fireEvent.click(await screen.findByRole('button', { name: 'History' }))
    expect(await screen.findByText(/HttpStatus: 503 Service Unavailable/)).toBeInTheDocument()
    expect(screen.getByText(/12\s*ms/)).toBeInTheDocument()
  })
})
