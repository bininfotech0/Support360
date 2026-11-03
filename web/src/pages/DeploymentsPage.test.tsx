import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { api } from '../api/client'
import { AuthContext } from '../auth/authContext'
import type { Overview } from '../api/types'
import { DeploymentsPage } from './DeploymentsPage'

const disabledOverview: Overview = {
  totalAgents: 0,
  onlineAgents: 0,
  offlineAgents: 0,
  activeAlerts: 0,
  criticalAlerts: 0,
  activeDeployments: 0,
  deploymentExecutionEnabled: false,
  monitoringCoveragePercent: 0,
  recentAlerts: [],
  recentEvents: [],
  generatedAt: '2026-10-30T12:00:00Z',
}

const admin = { id: 'u1', tenantId: 't1', tenantName: 'Tenant', isDemoTenant: false, email: 'admin@test', displayName: 'Admin', role: 'TenantAdmin' as const }
function renderPage() {
  return render(<AuthContext.Provider value={{ session: null, user: admin, login: vi.fn(), logout: vi.fn() }}><DeploymentsPage /></AuthContext.Provider>)
}

function mockChoices() {
  vi.spyOn(api, 'deploymentPackages').mockResolvedValue([])
  vi.spyOn(api, 'deployments').mockResolvedValue([])
  vi.spyOn(api, 'agents').mockResolvedValue([{ id: 'a1', hostname: 'server-1', status: 'Active' } as never])
  vi.spyOn(api, 'changeRequests').mockResolvedValue([{ id: 'c1', number: 'CHG-1', title: 'Nightly update', status: 'PendingApproval' } as never])
}

describe('DeploymentsPage', () => {
  afterEach(() => vi.restoreAllMocks())

  it('labels deployment execution as disabled and does not simulate deployments', async () => {
    vi.spyOn(api, 'overview').mockResolvedValue(disabledOverview)
    mockChoices()
    renderPage()

    expect(await screen.findByText('Deployment execution is not enabled in this build.')).toBeInTheDocument()
    expect(screen.getByText(/no deployment is simulated or claimed/i)).toBeInTheDocument()
    expect(screen.getByText('Not exposed')).toBeInTheDocument()
  })

  it('shows package upload controls and keeps execution disabled without a selected artifact', async () => {
    vi.spyOn(api, 'overview').mockResolvedValue(disabledOverview)
    mockChoices()
    renderPage()
    expect(await screen.findByLabelText('Package name')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Upload package' })).toBeDisabled()
    expect(screen.getByText(/Uploading a package does not authorize or execute it/)).toBeInTheDocument()
    expect(screen.getByText(/Requests can be linked to approved change records/i)).toBeInTheDocument()
  })

  it('creates a request bound to the chosen agent, package, and change request', async () => {
    vi.spyOn(api, 'overview').mockResolvedValue(disabledOverview)
    vi.spyOn(api, 'deploymentPackages').mockResolvedValue([{ id: 'p1', name: 'Web', version: '1.0', sha256: 'abc123', sizeBytes: 123, uploadedAt: '2026-10-30T12:00:00Z' }])
    vi.spyOn(api, 'deployments').mockResolvedValue([])
    vi.spyOn(api, 'agents').mockResolvedValue([{ id: 'a1', hostname: 'server-1', status: 'Active' } as never])
    vi.spyOn(api, 'changeRequests').mockResolvedValue([{ id: 'c1', number: 'CHG-1', title: 'Nightly update', status: 'PendingApproval' } as never])
    const create = vi.spyOn(api, 'createDeployment').mockResolvedValue({ id: 'd1' } as never)
    renderPage()
    fireEvent.change(await screen.findByLabelText('Target agent'), { target: { value: 'a1' } })
    fireEvent.change(screen.getByLabelText('Package'), { target: { value: 'p1' } })
    fireEvent.change(screen.getByLabelText('Change request'), { target: { value: 'c1' } })
    fireEvent.click(screen.getByRole('button', { name: 'Create request' }))
    await waitFor(() => expect(create).toHaveBeenCalledWith({ targetAgentId: 'a1', deploymentPackageId: 'p1', changeRequestId: 'c1' }))
  })
})
