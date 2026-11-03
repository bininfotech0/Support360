import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { api } from '../api/client'
import type { Overview } from '../api/types'
import { DashboardPage } from './DashboardPage'

vi.mock('../hooks/useRealtime', () => ({ useRealtime: () => false }))

const emptyOverview: Overview = {
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

function renderDashboard() {
  return render(
    <MemoryRouter>
      <DashboardPage />
    </MemoryRouter>,
  )
}

describe('DashboardPage', () => {
  afterEach(() => vi.restoreAllMocks())

  it('shows a loading state while the overview request is pending', () => {
    vi.spyOn(api, 'overview').mockReturnValue(new Promise(() => undefined))
    renderDashboard()
    expect(screen.getByRole('status')).toHaveTextContent('Loading overview')
  })

  it('shows API failures and retries the overview request', async () => {
    const overview = vi.spyOn(api, 'overview')
      .mockRejectedValueOnce(new Error('API unavailable'))
      .mockResolvedValueOnce(emptyOverview)
    renderDashboard()

    fireEvent.click(await screen.findByRole('button', { name: 'Retry' }))

    expect(await screen.findByRole('heading', { name: 'Operations overview' })).toBeInTheDocument()
    await waitFor(() => expect(overview).toHaveBeenCalledTimes(2))
  })

  it('renders truthful empty states and the disabled deployment gate', async () => {
    vi.spyOn(api, 'overview').mockResolvedValue(emptyOverview)
    renderDashboard()

    expect(await screen.findByText('No active alerts')).toBeInTheDocument()
    expect(screen.getByText('No activity yet')).toBeInTheDocument()
    expect(screen.getByText('Deployment execution is disabled.')).toBeInTheDocument()
    expect(screen.getByText('Total agents')).toBeInTheDocument()
    expect(screen.getAllByText('0').length).toBeGreaterThan(0)
  })
})
