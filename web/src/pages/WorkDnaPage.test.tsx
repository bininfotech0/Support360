import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { api } from '../api/client'
import type { WorkActivity, WorkCapacity, WorkDnaLookups } from '../api/types'
import { AuthContext } from '../auth/authContext'
import { WorkDnaPage } from './WorkDnaPage'

const admin = { id: 'u1', tenantId: 't1', tenantName: 'Tenant', isDemoTenant: false, email: 'admin@test', displayName: 'Admin', role: 'TenantAdmin' as const }
const viewer = { ...admin, id: 'u2', email: 'viewer@test', displayName: 'Viewer', role: 'Viewer' as const }

const lookups: WorkDnaLookups = {
  organizationName: 'Tenant',
  projects: [{ id: 'p1', name: 'ePortal Operations', status: 'Active' }],
  categories: [{ id: 'c1', categoryName: 'Monitoring', activityName: 'Daily health check' }],
  team: [{ id: 'u1', displayName: 'Admin', email: 'admin@test', role: 'TenantAdmin' }],
}

const capacity: WorkCapacity = {
  month: '2026-11',
  totalWorkingDays: 20,
  holidayDays: 1,
  leaveDays: 1,
  workingCapacityHours: 144,
  bookedHours: 4.5,
  availableHours: 139.5,
  utilizationPercent: 3.13,
  pendingActivities: 3,
  days: [
    { date: '2026-11-02', isOff: false, offLabel: null, total: 2, completed: 1, pending: 1, scheduled: 0, suppressed: 0 },
    { date: '2026-11-03', isOff: true, offLabel: 'Holiday', total: 1, completed: 0, pending: 1, scheduled: 0, suppressed: 1 },
  ],
  pendencyTimeline: [
    { date: '2026-11-02', bookedHours: 1.5, capacityHours: 8, pending: 1 },
    { date: '2026-11-03', bookedHours: 0, capacityHours: 0, pending: 0 },
  ],
}

const activity: WorkActivity = {
  id: 'a1',
  organization: 'Tenant',
  projectId: 'p1',
  projectName: 'ePortal Operations',
  assigneeId: 'u1',
  assigneeName: 'Admin',
  dnaCategoryId: 'c1',
  categoryName: 'Monitoring',
  activityName: 'Daily health check',
  title: 'Review payment exceptions',
  description: 'Check failed payments.',
  targetMode: 'Auto',
  effortMinutes: 90,
  effort: '1h 30m',
  severity: 'High',
  startTime: '2026-11-02T09:00:00Z',
  endTime: '2026-11-02T10:30:00Z',
  ignoreOff: false,
  businessImpact: 'Payment reconciliation SLA',
  status: 'Pending',
  completedAt: null,
  scheduleId: null,
  isSuppressed: false,
}

function mockPageData() {
  vi.spyOn(api, 'workDnaLookups').mockResolvedValue(lookups)
  vi.spyOn(api, 'workCapacity').mockResolvedValue(capacity)
  vi.spyOn(api, 'workCapacityRange').mockResolvedValue({ from: '2026-06', to: '2026-11', months: [capacity] })
  vi.spyOn(api, 'workActivities').mockResolvedValue([activity])
  vi.spyOn(api, 'workSchedules').mockResolvedValue([])
  vi.spyOn(api, 'workCalendarDays').mockResolvedValue([{ date: '2026-11-03', dayType: 'Holiday', label: 'Holiday' }])
  vi.spyOn(api, 'workLeaves').mockResolvedValue([])
}

function renderPage(user: typeof admin | typeof viewer = admin) {
  return render(
    <AuthContext.Provider value={{ session: null, user, login: vi.fn(), logout: vi.fn() }}>
      <WorkDnaPage />
    </AuthContext.Provider>,
  )
}

describe('WorkDnaPage', () => {
  afterEach(() => vi.restoreAllMocks())

  it('renders capacity, pulse, calendar, and activity DNA', async () => {
    mockPageData()
    renderPage()

    expect(await screen.findByText('Review payment exceptions')).toBeInTheDocument()
    expect(screen.getByText('144h')).toBeInTheDocument()
    expect(screen.getByText('4.5h')).toBeInTheDocument()
    expect(screen.getByText('Payment reconciliation SLA')).toBeInTheDocument()
    expect(screen.getAllByText('Holiday').length).toBeGreaterThan(0)
    expect(screen.getByText('1 suppressed')).toBeInTheDocument()
    expect(screen.getByText('Capacity trend')).toBeInTheDocument()
  })

  it('creates an activity with Work DNA fields', async () => {
    mockPageData()
    const create = vi.spyOn(api, 'createWorkActivity').mockResolvedValue(activity)
    renderPage()

    fireEvent.click(await screen.findByRole('button', { name: 'Add activity' }))
    fireEvent.change(screen.getByLabelText('Title'), { target: { value: 'Review payment exceptions' } })
    fireEvent.change(screen.getByLabelText('Business impact'), { target: { value: 'Payment reconciliation SLA' } })
    fireEvent.click(screen.getByRole('button', { name: 'Create activity' }))

    await waitFor(() =>
      expect(create).toHaveBeenCalledWith(
        expect.objectContaining({
          projectId: 'p1',
          assigneeId: 'u1',
          dnaCategoryId: 'c1',
          title: 'Review payment exceptions',
          targetMode: 'Auto',
          effortMinutes: 60,
          severity: 'Medium',
          ignoreOff: false,
          businessImpact: 'Payment reconciliation SLA',
        }),
      ),
    )
  })

  it('completes an activity', async () => {
    mockPageData()
    const complete = vi.spyOn(api, 'completeWorkActivity').mockResolvedValue({ ...activity, status: 'Completed' })
    renderPage()

    fireEvent.click(await screen.findByRole('button', { name: 'Complete' }))
    await waitFor(() => expect(complete).toHaveBeenCalledWith('a1'))
  })

  it('hides mutation controls from viewers', async () => {
    mockPageData()
    renderPage(viewer)

    expect(await screen.findByText('Review payment exceptions')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Add activity' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Add schedule' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Complete' })).not.toBeInTheDocument()
  })
})
