import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { AuthContext } from '../auth/authContext'
import { api } from '../api/client'
import { JobsPage } from './JobsPage'

vi.mock('../hooks/useRealtime', () => ({ useRealtime: () => false }))

const admin = { id: 'u1', tenantId: 't1', tenantName: 'Tenant', isDemoTenant: false, email: 'admin@test', displayName: 'Admin', role: 'TenantAdmin' as const }
const job = { id: 'j1', jobKey: 'daily-import', name: 'Daily import', scheduler: 'cron', expectedSchedule: '02:00 UTC', agentId: 'a1', hostname: 'host1', lastRunAt: null, lastStatus: null, lastExitCode: null, recentExecutions: [] }

function renderPage() {
  return render(<MemoryRouter><AuthContext.Provider value={{ session: null, user: admin, login: vi.fn(), logout: vi.fn() }}><JobsPage /></AuthContext.Provider></MemoryRouter>)
}

describe('JobsPage', () => {
  afterEach(() => vi.restoreAllMocks())

  it('configures a tenant job with an expected schedule', async () => {
    vi.spyOn(api, 'batchJobs').mockResolvedValue([])
    vi.spyOn(api, 'agents').mockResolvedValue([{ id: 'a1', hostname: 'host1', status: 'Active' } as never])
    const configure = vi.spyOn(api, 'configureBatchJob').mockResolvedValue(job)
    renderPage()
    fireEvent.change(await screen.findByLabelText('Agent'), { target: { value: 'a1' } })
    fireEvent.change(screen.getByLabelText('Job key'), { target: { value: 'daily-import' } })
    fireEvent.change(screen.getByLabelText('Name'), { target: { value: 'Daily import' } })
    fireEvent.change(screen.getByLabelText('Source scheduler'), { target: { value: 'cron' } })
    fireEvent.change(screen.getByLabelText('Expected schedule'), { target: { value: '02:00 UTC' } })
    fireEvent.click(screen.getByRole('button', { name: 'Configure job' }))
    await waitFor(() => expect(configure).toHaveBeenCalledWith({ agentId: 'a1', jobKey: 'daily-import', name: 'Daily import', scheduler: 'cron', expectedSchedule: '02:00 UTC' }))
  })

  it('edits the metadata while retaining the configured job key', async () => {
    vi.spyOn(api, 'batchJobs').mockResolvedValue([job])
    vi.spyOn(api, 'agents').mockResolvedValue([])
    const update = vi.spyOn(api, 'updateBatchJob').mockResolvedValue({ ...job, name: 'Daily import v2' })
    renderPage()
    fireEvent.click(await screen.findByRole('button', { name: 'Edit configuration' }))
    fireEvent.change(screen.getByLabelText('Name'), { target: { value: 'Daily import v2' } })
    fireEvent.click(screen.getByRole('button', { name: 'Save job' }))
    await waitFor(() => expect(update).toHaveBeenCalledWith('j1', { name: 'Daily import v2', scheduler: 'cron', expectedSchedule: '02:00 UTC' }))
  })
})
