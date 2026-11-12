import { useMemo, useState, type FormEvent } from 'react'
import { api } from '../api/client'
import { canManage, canOperate } from '../api/session'
import type {
  CreateWorkActivityRequest,
  CreateWorkActivityScheduleRequest,
  WorkActivity,
  WorkActivitySchedule,
} from '../api/types'
import { useAuth } from '../auth/authContext'
import { EmptyState, ErrorBanner, Loading, PageHeader, PageSection, StatCard, StatusBadge } from '../components/ui'
import { useLoad } from '../hooks/useLoad'
import { formatNumber, formatPercent } from '../utils'

const weekdays = ['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday', 'Sunday']
const severityOptions = ['Low', 'Medium', 'High', 'Critical']
const targetModes = ['Auto', 'Manual']
const statusOptions = ['Pending', 'Scheduled', 'Completed', 'Overdue']
const statusFilters = ['all', ...statusOptions]

function pad(value: number): string {
  return String(value).padStart(2, '0')
}

function monthValue(date = new Date()): string {
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}`
}

function todayValue(): string {
  const date = new Date()
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`
}

function localDateTime(iso: string | null | undefined): string {
  const date = iso ? new Date(iso) : new Date()
  if (!iso) date.setHours(9, 0, 0, 0)
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}T${pad(date.getHours())}:${pad(date.getMinutes())}`
}

function toIso(local: string): string {
  return new Date(local).toISOString()
}

function shiftMonth(month: string, offset: number): string {
  const [year, monthNumber] = month.split('-').map(Number)
  return monthValue(new Date(year, monthNumber - 1 + offset, 1))
}

function monthLabel(month: string): string {
  const [year, monthNumber] = month.split('-').map(Number)
  return new Intl.DateTimeFormat(undefined, { month: 'long', year: 'numeric' }).format(
    new Date(year, monthNumber - 1, 1),
  )
}

interface ActivityDraft {
  projectId: string
  assigneeId: string
  dnaCategoryId: string
  title: string
  description: string
  targetMode: string
  effortMinutes: string
  severity: string
  startTime: string
  endTime: string
  ignoreOff: boolean
  businessImpact: string
}

interface ScheduleDraft {
  projectId: string
  assigneeId: string
  dnaCategoryId: string
  title: string
  description: string
  targetMode: string
  effortMinutes: string
  severity: string
  ignoreOff: boolean
  businessImpact: string
  frequency: string
  weekdays: string[]
  dayOfMonth: string
  startHourUtc: string
  startDate: string
  endDate: string
  isActive: boolean
}

function emptyActivityDraft(): ActivityDraft {
  return {
    projectId: '',
    assigneeId: '',
    dnaCategoryId: '',
    title: '',
    description: '',
    targetMode: 'Auto',
    effortMinutes: '60',
    severity: 'Medium',
    startTime: localDateTime(null),
    endTime: localDateTime(new Date(Date.now() + 60 * 60 * 1000).toISOString()),
    ignoreOff: false,
    businessImpact: '',
  }
}

function emptyScheduleDraft(): ScheduleDraft {
  return {
    projectId: '',
    assigneeId: '',
    dnaCategoryId: '',
    title: '',
    description: '',
    targetMode: 'Auto',
    effortMinutes: '60',
    severity: 'Medium',
    ignoreOff: false,
    businessImpact: '',
    frequency: 'Daily',
    weekdays: ['Monday'],
    dayOfMonth: '1',
    startHourUtc: '9',
    startDate: todayValue(),
    endDate: `${new Date().getFullYear() + 1}-12-31`,
    isActive: true,
  }
}

export function WorkDnaPage() {
  const { user } = useAuth()
  const [month, setMonth] = useState(monthValue())
  const [statusFilter, setStatusFilter] = useState('all')
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  const lookups = useLoad('work-dna:lookups', api.workDnaLookups)
  const capacity = useLoad(`work-dna:capacity:${month}`, () => api.workCapacity(month))
  const capacityRange = useLoad(`work-dna:capacity-range:${month}`, () =>
    api.workCapacityRange(shiftMonth(month, -5), month),
  )
  const activities = useLoad(`work-dna:activities:${month}`, () => api.workActivities(month))
  const schedules = useLoad('work-dna:schedules', api.workSchedules)
  const holidays = useLoad(`work-dna:calendar:${month}`, () => api.workCalendarDays(month))
  const leaves = useLoad(`work-dna:leaves:${month}`, () => api.workLeaves(month))

  const [activityDraft, setActivityDraft] = useState<ActivityDraft | null>(null)
  const [editingActivityId, setEditingActivityId] = useState<string | null>(null)
  const [scheduleDraft, setScheduleDraft] = useState<ScheduleDraft | null>(null)
  const [editingScheduleId, setEditingScheduleId] = useState<string | null>(null)
  const [holidayDate, setHolidayDate] = useState(todayValue())
  const [holidayType, setHolidayType] = useState('Holiday')
  const [holidayLabel, setHolidayLabel] = useState('')
  const [leaveUserId, setLeaveUserId] = useState('')
  const [leaveDate, setLeaveDate] = useState(todayValue())
  const [leaveHours, setLeaveHours] = useState('8')
  const [leaveReason, setLeaveReason] = useState('')

  const filteredActivities = useMemo(
    () => (activities.data ?? []).filter((activity) => statusFilter === 'all' || activity.status === statusFilter),
    [activities.data, statusFilter],
  )

  const calendarCells = useMemo(() => {
    const days = capacity.data?.days ?? []
    if (days.length === 0) return []
    const leading = new Date(`${days[0].date}T00:00:00`).getDay()
    return [...Array.from({ length: leading }, () => null), ...days]
  }, [capacity.data])

  const refreshAll = () => {
    capacity.refresh()
    capacityRange.refresh()
    activities.refresh()
    schedules.refresh()
    holidays.refresh()
    leaves.refresh()
  }

  const openCreateActivity = () => {
    const base = emptyActivityDraft()
    setActivityDraft({
      ...base,
      projectId: lookups.data?.projects[0]?.id ?? '',
      assigneeId: lookups.data?.team[0]?.id ?? '',
      dnaCategoryId: lookups.data?.categories[0]?.id ?? '',
    })
    setEditingActivityId(null)
  }

  const openEditActivity = (activity: WorkActivity) => {
    setActivityDraft({
      projectId: activity.projectId,
      assigneeId: activity.assigneeId,
      dnaCategoryId: activity.dnaCategoryId,
      title: activity.title,
      description: activity.description ?? '',
      targetMode: activity.targetMode,
      effortMinutes: String(activity.effortMinutes),
      severity: activity.severity,
      startTime: localDateTime(activity.startTime),
      endTime: localDateTime(activity.endTime),
      ignoreOff: activity.ignoreOff,
      businessImpact: activity.businessImpact ?? '',
    })
    setEditingActivityId(activity.id)
  }

  const saveActivity = async (event: FormEvent) => {
    event.preventDefault()
    if (!activityDraft) return
    setBusy(true)
    setError(null)
    try {
      const payload: CreateWorkActivityRequest = {
        projectId: activityDraft.projectId,
        assigneeId: activityDraft.assigneeId,
        dnaCategoryId: activityDraft.dnaCategoryId,
        title: activityDraft.title,
        description: activityDraft.description || null,
        targetMode: activityDraft.targetMode,
        effortMinutes: Number(activityDraft.effortMinutes),
        severity: activityDraft.severity,
        startTime: toIso(activityDraft.startTime),
        endTime: toIso(activityDraft.endTime),
        ignoreOff: activityDraft.ignoreOff,
        businessImpact: activityDraft.businessImpact,
      }
      if (editingActivityId) {
        await api.updateWorkActivity(editingActivityId, payload)
      } else {
        await api.createWorkActivity(payload)
      }
      setActivityDraft(null)
      setEditingActivityId(null)
      refreshAll()
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : 'Could not save work activity.')
    } finally {
      setBusy(false)
    }
  }

  const completeActivity = async (activity: WorkActivity) => {
    setBusy(true)
    setError(null)
    try {
      await api.completeWorkActivity(activity.id)
      refreshAll()
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : 'Could not complete work activity.')
    } finally {
      setBusy(false)
    }
  }

  const openCreateSchedule = () => {
    const base = emptyScheduleDraft()
    setScheduleDraft({
      ...base,
      projectId: lookups.data?.projects[0]?.id ?? '',
      assigneeId: lookups.data?.team[0]?.id ?? '',
      dnaCategoryId: lookups.data?.categories[0]?.id ?? '',
    })
    setEditingScheduleId(null)
  }

  const openEditSchedule = (schedule: WorkActivitySchedule) => {
    setScheduleDraft({
      projectId: schedule.projectId,
      assigneeId: schedule.assigneeId,
      dnaCategoryId: schedule.dnaCategoryId,
      title: schedule.title,
      description: schedule.description ?? '',
      targetMode: schedule.targetMode,
      effortMinutes: String(schedule.effortMinutes),
      severity: schedule.severity,
      ignoreOff: schedule.ignoreOff,
      businessImpact: schedule.businessImpact ?? '',
      frequency: schedule.frequency,
      weekdays: schedule.weekdays.length > 0 ? schedule.weekdays : ['Monday'],
      dayOfMonth: String(schedule.dayOfMonth ?? 1),
      startHourUtc: String(schedule.startHourUtc),
      startDate: schedule.startDate,
      endDate: schedule.endDate,
      isActive: schedule.isActive,
    })
    setEditingScheduleId(schedule.id)
  }

  const saveSchedule = async (event: FormEvent) => {
    event.preventDefault()
    if (!scheduleDraft) return
    setBusy(true)
    setError(null)
    try {
      const payload: CreateWorkActivityScheduleRequest = {
        projectId: scheduleDraft.projectId,
        assigneeId: scheduleDraft.assigneeId,
        dnaCategoryId: scheduleDraft.dnaCategoryId,
        title: scheduleDraft.title,
        description: scheduleDraft.description || null,
        targetMode: scheduleDraft.targetMode,
        effortMinutes: Number(scheduleDraft.effortMinutes),
        severity: scheduleDraft.severity,
        ignoreOff: scheduleDraft.ignoreOff,
        businessImpact: scheduleDraft.businessImpact,
        frequency: scheduleDraft.frequency,
        weekdays: scheduleDraft.frequency === 'Weekly' ? scheduleDraft.weekdays : [],
        dayOfMonth: scheduleDraft.frequency === 'Monthly' ? Number(scheduleDraft.dayOfMonth) : null,
        startHourUtc: Number(scheduleDraft.startHourUtc),
        startDate: scheduleDraft.startDate,
        endDate: scheduleDraft.endDate,
      }
      if (editingScheduleId) {
        await api.updateWorkSchedule(editingScheduleId, { ...payload, isActive: scheduleDraft.isActive })
      } else {
        await api.createWorkSchedule(payload)
      }
      setScheduleDraft(null)
      setEditingScheduleId(null)
      refreshAll()
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : 'Could not save recurring schedule.')
    } finally {
      setBusy(false)
    }
  }

  const materialize = async () => {
    setBusy(true)
    setError(null)
    try {
      await api.materializeWorkSchedules(month)
      refreshAll()
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : 'Could not materialize schedules.')
    } finally {
      setBusy(false)
    }
  }

  const saveHoliday = async (event: FormEvent) => {
    event.preventDefault()
    setBusy(true)
    setError(null)
    try {
      await api.setWorkCalendarDay({ date: holidayDate, dayType: holidayType, label: holidayLabel || null })
      setHolidayLabel('')
      refreshAll()
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : 'Could not save calendar day.')
    } finally {
      setBusy(false)
    }
  }

  const removeHoliday = async (date: string) => {
    setBusy(true)
    setError(null)
    try {
      await api.removeWorkCalendarDay(date)
      refreshAll()
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : 'Could not remove calendar day.')
    } finally {
      setBusy(false)
    }
  }

  const saveLeave = async (event: FormEvent) => {
    event.preventDefault()
    setBusy(true)
    setError(null)
    try {
      await api.createWorkLeave({
        userId: leaveUserId || lookups.data?.team[0]?.id || '',
        date: leaveDate,
        hours: Number(leaveHours),
        reason: leaveReason || null,
      })
      setLeaveReason('')
      refreshAll()
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : 'Could not save leave.')
    } finally {
      setBusy(false)
    }
  }

  const toggleWeekday = (day: string) => {
    if (!scheduleDraft) return
    const selected = scheduleDraft.weekdays.includes(day)
    setScheduleDraft({
      ...scheduleDraft,
      weekdays: selected ? scheduleDraft.weekdays.filter((item) => item !== day) : [...scheduleDraft.weekdays, day],
    })
  }

  return (
    <>
      <PageHeader
        eyebrow="Capacity engine"
        title="Work DNA"
        description="Plan recurring work, capture the DNA of every activity, and track capacity, pendency, and OFF days for the month."
        actions={
          <div className="inline-actions">
            <button type="button" className="button button-ghost" onClick={() => setMonth(shiftMonth(month, -1))}>
              Previous
            </button>
            <input
              type="month"
              value={month}
              onChange={(event) => {
                if (event.target.value) setMonth(event.target.value)
              }}
              aria-label="Work DNA month"
            />
            <button type="button" className="button button-ghost" onClick={() => setMonth(shiftMonth(month, 1))}>
              Next
            </button>
            <button type="button" className="button button-ghost" onClick={refreshAll}>
              Refresh
            </button>
          </div>
        }
      />

      {error ? <ErrorBanner message={error} /> : null}
      {lookups.error ? <ErrorBanner message={lookups.error} onRetry={lookups.refresh} /> : null}

      <div className="stats-grid">
        <StatCard
          label="Working capacity"
          value={`${formatNumber(capacity.data?.workingCapacityHours ?? 0)}h`}
          detail={`${capacity.data?.totalWorkingDays ?? 0} working days - ${capacity.data?.holidayDays ?? 0} holidays - ${capacity.data?.leaveDays ?? 0} leave days`}
        />
        <StatCard label="Booked effort" value={`${formatNumber(capacity.data?.bookedHours ?? 0)}h`} detail={monthLabel(month)} tone="neutral" />
        <StatCard
          label="Available"
          value={`${formatNumber(capacity.data?.availableHours ?? 0)}h`}
          detail={`${formatPercent(capacity.data?.utilizationPercent ?? 0)} utilized`}
          tone={(capacity.data?.utilizationPercent ?? 0) > 90 ? 'bad' : (capacity.data?.utilizationPercent ?? 0) > 75 ? 'warn' : 'ok'}
        />
        <StatCard label="Pendency" value={capacity.data?.pendingActivities ?? 0} detail="Scheduled, pending, or overdue activities" tone="warn" />
      </div>

      <PageSection
        title="Capacity trend"
        description="Booked effort against working capacity across the last six months."
      >
        {capacityRange.loading && !capacityRange.data ? <Loading label="Loading capacity trend" /> : null}
        {capacityRange.error ? <ErrorBanner message={capacityRange.error} onRetry={capacityRange.refresh} /> : null}
        {capacityRange.data ? (
          <div className="capacity-trend">
            {capacityRange.data.months.map((entry) => (
              <div className="capacity-trend-month" key={entry.month}>
                <span className="muted">{monthLabel(entry.month)}</span>
                <strong>{formatPercent(entry.utilizationPercent)}</strong>
                <div className="capacity-trend-track" aria-hidden="true">
                  <span
                    className="capacity-trend-fill"
                    style={{ width: `${Math.min(100, Math.max(0, entry.utilizationPercent))}%` }}
                  />
                </div>
                <span className="muted block">
                  {formatNumber(entry.bookedHours)}h of {formatNumber(entry.workingCapacityHours)}h
                </span>
              </div>
            ))}
          </div>
        ) : null}
      </PageSection>

      <PageSection
        title="Work Pulse"
        description="Daily booked effort against the 8-hour working capacity, with remaining pendency."
        actions={
          canOperate(user) ? (
            <button type="button" className="button button-ghost" onClick={materialize} disabled={busy}>
              Materialize schedules
            </button>
          ) : null
        }
      >
        {capacity.loading && !capacity.data ? <Loading label="Loading capacity" /> : null}
        {capacity.error ? <ErrorBanner message={capacity.error} onRetry={capacity.refresh} /> : null}
        {capacity.data ? (
          <div className="work-pulse">
            {capacity.data.pendencyTimeline.map((point) => {
              const max = Math.max(
                ...capacity.data!.pendencyTimeline.map((item) => Math.max(item.capacityHours, item.bookedHours)),
                1,
              )
              return (
                <div className="work-pulse-day" key={point.date}>
                  <div className="work-pulse-bars">
                    <span
                      className="work-pulse-bar work-pulse-capacity"
                      style={{ height: `${Math.max(4, (point.capacityHours / max) * 100)}%` }}
                      title={`Capacity ${point.capacityHours}h`}
                    />
                    <span
                      className="work-pulse-bar work-pulse-booked"
                      style={{ height: `${Math.max(4, (point.bookedHours / max) * 100)}%` }}
                      title={`Booked ${point.bookedHours}h`}
                    />
                  </div>
                  <span className="work-pulse-date">{point.date.slice(8)}</span>
                  <span className="work-pulse-pending">{point.pending}</span>
                </div>
              )
            })}
          </div>
        ) : null}
      </PageSection>

      <PageSection title="Monthly calendar" description={`${monthLabel(month)} - OFF days, holidays, and daily activity load.`}>
        {capacity.data ? (
          <>
            <div className="calendar-grid calendar-head">
              {['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat'].map((day) => (
                <span key={day}>{day}</span>
              ))}
            </div>
            <div className="calendar-grid">
              {calendarCells.map((day, index) =>
                day ? (
                  <div key={day.date} className={`calendar-day ${day.isOff ? 'is-off' : ''}`}>
                    <div className="calendar-day-head">
                      <strong>{day.date.slice(8)}</strong>
                      {day.isOff ? <span className="badge badge-neutral">OFF</span> : null}
                    </div>
                    {day.offLabel ? <span className="muted block">{day.offLabel}</span> : null}
                    <div className="calendar-day-stats">
                      <span>{day.total} total</span>
                      <span>{day.completed} done</span>
                      {day.scheduled > 0 ? <span>{day.scheduled} scheduled</span> : null}
                      {day.pending > 0 ? <span>{day.pending} pending</span> : null}
                      {day.suppressed > 0 ? <span>{day.suppressed} suppressed</span> : null}
                    </div>
                  </div>
                ) : (
                  <div key={`empty-${index}`} className="calendar-day is-empty" />
                ),
              )}
            </div>
          </>
        ) : (
          <EmptyState title="Calendar unavailable" detail="Capacity data is required to build the monthly calendar." />
        )}
      </PageSection>

      <PageSection
        title="Work activities"
        description="Every activity carries Work DNA: target mode, effort, severity, business impact, and OFF handling."
        actions={
          <div className="inline-actions">
            <select value={statusFilter} onChange={(event) => setStatusFilter(event.target.value)} aria-label="Activity status filter">
              {statusFilters.map((value) => (
                <option key={value} value={value}>
                  {value === 'all' ? 'All statuses' : value}
                </option>
              ))}
            </select>
            {canOperate(user) ? (
              <button type="button" className="button button-primary" onClick={openCreateActivity}>
                Add activity
              </button>
            ) : null}
          </div>
        }
      >
        {activities.loading && !activities.data ? <Loading label="Loading activities" /> : null}
        {activities.error ? <ErrorBanner message={activities.error} onRetry={activities.refresh} /> : null}
        {!activities.loading && filteredActivities.length === 0 ? (
          <EmptyState title="No work activities" detail="Add an activity to capture its Work DNA and plan effort." />
        ) : null}
        {filteredActivities.length > 0 ? (
          <div className="table-wrap">
            <table>
              <thead>
                <tr>
                  <th>Activity</th>
                  <th>DNA</th>
                  <th>Owner</th>
                  <th>Target</th>
                  <th>Effort</th>
                  <th>Severity</th>
                  <th>Status</th>
                  <th>Actions</th>
                </tr>
              </thead>
              <tbody>
                {filteredActivities.map((activity) => (
                  <tr key={activity.id}>
                    <td>
                      <strong>{activity.title}</strong>
                      <span className="muted block">
                        {activity.projectName} - {activity.organization}
                      </span>
                      <span className="muted block">{activity.businessImpact}</span>
                    </td>
                    <td>
                      {activity.categoryName}
                      <span className="muted block">{activity.activityName}</span>
                    </td>
                    <td>{activity.assigneeName}</td>
                    <td>{activity.targetMode}</td>
                    <td>{activity.effort}</td>
                    <td>
                      <StatusBadge value={activity.severity} />
                    </td>
                    <td>
                      <StatusBadge value={activity.status} />
                      {activity.ignoreOff ? <span className="muted block">Ignore OFF</span> : null}
                      {activity.isSuppressed ? <span className="muted block">Suppressed by OFF day</span> : null}
                    </td>
                    <td>
                      {canOperate(user) ? (
                        <div className="inline-actions">
                          <button type="button" className="button button-ghost" onClick={() => openEditActivity(activity)}>
                            Edit
                          </button>
                          {activity.status !== 'Completed' ? (
                            <button
                              type="button"
                              className="button button-ghost"
                              onClick={() => completeActivity(activity)}
                              disabled={busy}
                            >
                              Complete
                            </button>
                          ) : null}
                        </div>
                      ) : (
                        '-'
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        ) : null}
      </PageSection>

      <PageSection
        title="Recurring schedules"
        description="Daily, weekly, and monthly work is materialized into dated activities for the selected month."
        actions={
          canOperate(user) ? (
            <button type="button" className="button button-primary" onClick={openCreateSchedule}>
              Add schedule
            </button>
          ) : null
        }
      >
        {schedules.loading && !schedules.data ? <Loading label="Loading schedules" /> : null}
        {schedules.error ? <ErrorBanner message={schedules.error} onRetry={schedules.refresh} /> : null}
        {!schedules.loading && (schedules.data?.length ?? 0) === 0 ? (
          <EmptyState title="No recurring schedules" detail="Create a schedule to generate work automatically each period." />
        ) : null}
        {(schedules.data?.length ?? 0) > 0 ? (
          <div className="table-wrap">
            <table>
              <thead>
                <tr>
                  <th>Schedule</th>
                  <th>Owner</th>
                  <th>Frequency</th>
                  <th>Window</th>
                  <th>Effort</th>
                  <th>Active</th>
                  <th>Actions</th>
                </tr>
              </thead>
              <tbody>
                {schedules.data?.map((schedule) => (
                  <tr key={schedule.id}>
                    <td>
                      <strong>{schedule.title}</strong>
                      <span className="muted block">
                        {schedule.categoryName} - {schedule.activityName}
                      </span>
                    </td>
                    <td>
                      {schedule.assigneeName}
                      <span className="muted block">{schedule.projectName}</span>
                    </td>
                    <td>
                      {schedule.frequency}
                      <span className="muted block">
                        {schedule.frequency === 'Weekly'
                          ? schedule.weekdays.join(', ')
                          : schedule.frequency === 'Monthly'
                            ? `Day ${schedule.dayOfMonth}`
                            : `${pad(schedule.startHourUtc)}:00 UTC`}
                      </span>
                    </td>
                    <td>
                      {schedule.startDate} to {schedule.endDate}
                    </td>
                    <td>{schedule.effort}</td>
                    <td>
                      <StatusBadge value={schedule.isActive ? 'Active' : 'Stopped'} />
                    </td>
                    <td>
                      {canOperate(user) ? (
                        <div className="inline-actions">
                          <button type="button" className="button button-ghost" onClick={() => openEditSchedule(schedule)}>
                            Edit
                          </button>
                          <button
                            type="button"
                            className="button button-ghost"
                            onClick={async () => {
                              setBusy(true)
                              try {
                                await api.materializeWorkSchedule(schedule.id, month)
                                refreshAll()
                              } catch (caught) {
                                setError(caught instanceof Error ? caught.message : 'Could not materialize schedule.')
                              } finally {
                                setBusy(false)
                              }
                            }}
                            disabled={busy}
                          >
                            Generate
                          </button>
                        </div>
                      ) : (
                        '-'
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        ) : null}
      </PageSection>

      <div className="work-dna-secondary">
        <PageSection title="Holidays and OFF days" description="Override the default weekend calendar for the tenant.">
          {canManage(user) ? (
            <form className="enrollment-controls" onSubmit={saveHoliday}>
              <label>
                Date
                <input type="date" required value={holidayDate} onChange={(event) => setHolidayDate(event.target.value)} />
              </label>
              <label>
                Day type
                <select value={holidayType} onChange={(event) => setHolidayType(event.target.value)}>
                  <option value="Holiday">Holiday</option>
                  <option value="Off">OFF</option>
                  <option value="Working">Working</option>
                </select>
              </label>
              <label>
                Label
                <input maxLength={120} value={holidayLabel} onChange={(event) => setHolidayLabel(event.target.value)} />
              </label>
              <button type="submit" className="button button-primary" disabled={busy}>
                Save day
              </button>
            </form>
          ) : null}
          {holidays.error ? <ErrorBanner message={holidays.error} onRetry={holidays.refresh} /> : null}
          {!holidays.loading && (holidays.data?.length ?? 0) === 0 ? (
            <EmptyState title="No calendar overrides" detail="Weekends are OFF by default. Add holidays or working days as needed." />
          ) : null}
          {(holidays.data?.length ?? 0) > 0 ? (
            <ul className="compact-list">
              {holidays.data?.map((day) => (
                <li key={day.date}>
                  <span>
                    <strong>{day.date}</strong> {day.dayType}
                    {day.label ? ` - ${day.label}` : ''}
                  </span>
                  {canManage(user) ? (
                    <button type="button" className="button button-ghost" onClick={() => removeHoliday(day.date)} disabled={busy}>
                      Remove
                    </button>
                  ) : null}
                </li>
              ))}
            </ul>
          ) : null}
        </PageSection>

        <PageSection title="Leaves" description="Leave days reduce the tenant's monthly working capacity.">
          {canOperate(user) ? (
            <form className="enrollment-controls" onSubmit={saveLeave}>
              <label>
                Team member
                <select value={leaveUserId} onChange={(event) => setLeaveUserId(event.target.value)} required>
                  <option value="">Select team member</option>
                  {lookups.data?.team.map((member) => (
                    <option key={member.id} value={member.id}>
                      {member.displayName}
                    </option>
                  ))}
                </select>
              </label>
              <label>
                Date
                <input type="date" required value={leaveDate} onChange={(event) => setLeaveDate(event.target.value)} />
              </label>
              <label>
                Hours
                <input
                  type="number"
                  min={1}
                  max={24}
                  required
                  value={leaveHours}
                  onChange={(event) => setLeaveHours(event.target.value)}
                />
              </label>
              <label>
                Reason
                <input maxLength={500} value={leaveReason} onChange={(event) => setLeaveReason(event.target.value)} />
              </label>
              <button type="submit" className="button button-primary" disabled={busy}>
                Add leave
              </button>
            </form>
          ) : null}
          {leaves.error ? <ErrorBanner message={leaves.error} onRetry={leaves.refresh} /> : null}
          {!leaves.loading && (leaves.data?.length ?? 0) === 0 ? (
            <EmptyState title="No leave recorded" detail="Add leave to adjust available capacity for the month." />
          ) : null}
          {(leaves.data?.length ?? 0) > 0 ? (
            <ul className="compact-list">
              {leaves.data?.map((leave) => (
                <li key={leave.id}>
                  <span>
                    <strong>{leave.userDisplayName}</strong> - {leave.date} - {leave.hours}h
                    {leave.reason ? ` - ${leave.reason}` : ''}
                  </span>
                </li>
              ))}
            </ul>
          ) : null}
        </PageSection>
      </div>

      {activityDraft ? (
        <div className="modal-backdrop" role="presentation">
          <div className="modal" role="dialog" aria-modal="true" aria-label={editingActivityId ? 'Edit activity' : 'Add activity'}>
            <div className="modal-heading">
              <div>
                <h2>{editingActivityId ? 'Edit activity' : 'Add activity'}</h2>
                <p>Capture the Work DNA used by capacity and pendency analytics.</p>
              </div>
              <button type="button" className="button button-ghost" onClick={() => setActivityDraft(null)}>
                Close
              </button>
            </div>
            <form className="modal-form" onSubmit={saveActivity}>
              <label>
                Title
                <input
                  required
                  maxLength={255}
                  value={activityDraft.title}
                  onChange={(event) => setActivityDraft({ ...activityDraft, title: event.target.value })}
                />
              </label>
              <div className="modal-grid">
                <label>
                  Project
                  <select
                    required
                    value={activityDraft.projectId}
                    onChange={(event) => setActivityDraft({ ...activityDraft, projectId: event.target.value })}
                  >
                    {lookups.data?.projects.map((project) => (
                      <option key={project.id} value={project.id}>
                        {project.name}
                      </option>
                    ))}
                  </select>
                </label>
                <label>
                  Assignee
                  <select
                    required
                    value={activityDraft.assigneeId}
                    onChange={(event) => setActivityDraft({ ...activityDraft, assigneeId: event.target.value })}
                  >
                    {lookups.data?.team.map((member) => (
                      <option key={member.id} value={member.id}>
                        {member.displayName}
                      </option>
                    ))}
                  </select>
                </label>
                <label>
                  DNA category
                  <select
                    required
                    value={activityDraft.dnaCategoryId}
                    onChange={(event) => setActivityDraft({ ...activityDraft, dnaCategoryId: event.target.value })}
                  >
                    {lookups.data?.categories.map((category) => (
                      <option key={category.id} value={category.id}>
                        {category.categoryName} - {category.activityName}
                      </option>
                    ))}
                  </select>
                </label>
                <label>
                  Target mode
                  <select
                    value={activityDraft.targetMode}
                    onChange={(event) => setActivityDraft({ ...activityDraft, targetMode: event.target.value })}
                  >
                    {targetModes.map((mode) => (
                      <option key={mode} value={mode}>
                        {mode}
                      </option>
                    ))}
                  </select>
                </label>
                <label>
                  Effort (minutes)
                  <input
                    type="number"
                    min={15}
                    max={1440}
                    required
                    value={activityDraft.effortMinutes}
                    onChange={(event) => setActivityDraft({ ...activityDraft, effortMinutes: event.target.value })}
                  />
                </label>
                <label>
                  Severity
                  <select
                    value={activityDraft.severity}
                    onChange={(event) => setActivityDraft({ ...activityDraft, severity: event.target.value })}
                  >
                    {severityOptions.map((severity) => (
                      <option key={severity} value={severity}>
                        {severity}
                      </option>
                    ))}
                  </select>
                </label>
                <label>
                  Start
                  <input
                    type="datetime-local"
                    required
                    value={activityDraft.startTime}
                    onChange={(event) => setActivityDraft({ ...activityDraft, startTime: event.target.value })}
                  />
                </label>
                <label>
                  End
                  <input
                    type="datetime-local"
                    required
                    value={activityDraft.endTime}
                    onChange={(event) => setActivityDraft({ ...activityDraft, endTime: event.target.value })}
                  />
                </label>
              </div>
              <label>
                Description
                <textarea
                  maxLength={8000}
                  value={activityDraft.description}
                  onChange={(event) => setActivityDraft({ ...activityDraft, description: event.target.value })}
                />
              </label>
              <label>
                Business impact
                <textarea
                  required
                  maxLength={4000}
                  value={activityDraft.businessImpact}
                  onChange={(event) => setActivityDraft({ ...activityDraft, businessImpact: event.target.value })}
                />
              </label>
              <label className="checkbox-row">
                <input
                  type="checkbox"
                  checked={activityDraft.ignoreOff}
                  onChange={(event) => setActivityDraft({ ...activityDraft, ignoreOff: event.target.checked })}
                />
                Ignore OFF
              </label>
              <div className="inline-actions">
                <button type="submit" className="button button-primary" disabled={busy}>
                  {editingActivityId ? 'Save activity' : 'Create activity'}
                </button>
                <button type="button" className="button button-ghost" onClick={() => setActivityDraft(null)}>
                  Cancel
                </button>
              </div>
            </form>
          </div>
        </div>
      ) : null}

      {scheduleDraft ? (
        <div className="modal-backdrop" role="presentation">
          <div className="modal" role="dialog" aria-modal="true" aria-label={editingScheduleId ? 'Edit schedule' : 'Add schedule'}>
            <div className="modal-heading">
              <div>
                <h2>{editingScheduleId ? 'Edit schedule' : 'Add schedule'}</h2>
                <p>Recurring work is generated for the selected month when materialized.</p>
              </div>
              <button type="button" className="button button-ghost" onClick={() => setScheduleDraft(null)}>
                Close
              </button>
            </div>
            <form className="modal-form" onSubmit={saveSchedule}>
              <label>
                Title
                <input
                  required
                  maxLength={255}
                  value={scheduleDraft.title}
                  onChange={(event) => setScheduleDraft({ ...scheduleDraft, title: event.target.value })}
                />
              </label>
              <div className="modal-grid">
                <label>
                  Project
                  <select
                    required
                    value={scheduleDraft.projectId}
                    onChange={(event) => setScheduleDraft({ ...scheduleDraft, projectId: event.target.value })}
                  >
                    {lookups.data?.projects.map((project) => (
                      <option key={project.id} value={project.id}>
                        {project.name}
                      </option>
                    ))}
                  </select>
                </label>
                <label>
                  Assignee
                  <select
                    required
                    value={scheduleDraft.assigneeId}
                    onChange={(event) => setScheduleDraft({ ...scheduleDraft, assigneeId: event.target.value })}
                  >
                    {lookups.data?.team.map((member) => (
                      <option key={member.id} value={member.id}>
                        {member.displayName}
                      </option>
                    ))}
                  </select>
                </label>
                <label>
                  DNA category
                  <select
                    required
                    value={scheduleDraft.dnaCategoryId}
                    onChange={(event) => setScheduleDraft({ ...scheduleDraft, dnaCategoryId: event.target.value })}
                  >
                    {lookups.data?.categories.map((category) => (
                      <option key={category.id} value={category.id}>
                        {category.categoryName} - {category.activityName}
                      </option>
                    ))}
                  </select>
                </label>
                <label>
                  Target mode
                  <select
                    value={scheduleDraft.targetMode}
                    onChange={(event) => setScheduleDraft({ ...scheduleDraft, targetMode: event.target.value })}
                  >
                    {targetModes.map((mode) => (
                      <option key={mode} value={mode}>
                        {mode}
                      </option>
                    ))}
                  </select>
                </label>
                <label>
                  Effort (minutes)
                  <input
                    type="number"
                    min={15}
                    max={1440}
                    required
                    value={scheduleDraft.effortMinutes}
                    onChange={(event) => setScheduleDraft({ ...scheduleDraft, effortMinutes: event.target.value })}
                  />
                </label>
                <label>
                  Severity
                  <select
                    value={scheduleDraft.severity}
                    onChange={(event) => setScheduleDraft({ ...scheduleDraft, severity: event.target.value })}
                  >
                    {severityOptions.map((severity) => (
                      <option key={severity} value={severity}>
                        {severity}
                      </option>
                    ))}
                  </select>
                </label>
                <label>
                  Frequency
                  <select
                    value={scheduleDraft.frequency}
                    onChange={(event) => setScheduleDraft({ ...scheduleDraft, frequency: event.target.value })}
                  >
                    <option value="Daily">Daily</option>
                    <option value="Weekly">Weekly</option>
                    <option value="Monthly">Monthly</option>
                  </select>
                </label>
                <label>
                  Start hour (UTC)
                  <input
                    type="number"
                    min={0}
                    max={23}
                    required
                    value={scheduleDraft.startHourUtc}
                    onChange={(event) => setScheduleDraft({ ...scheduleDraft, startHourUtc: event.target.value })}
                  />
                </label>
                <label>
                  Start date
                  <input
                    type="date"
                    required
                    value={scheduleDraft.startDate}
                    onChange={(event) => setScheduleDraft({ ...scheduleDraft, startDate: event.target.value })}
                  />
                </label>
                <label>
                  End date
                  <input
                    type="date"
                    required
                    value={scheduleDraft.endDate}
                    onChange={(event) => setScheduleDraft({ ...scheduleDraft, endDate: event.target.value })}
                  />
                </label>
              </div>
              {scheduleDraft.frequency === 'Weekly' ? (
                <div className="weekday-picker">
                  {weekdays.map((day) => (
                    <label key={day} className="checkbox-row">
                      <input
                        type="checkbox"
                        checked={scheduleDraft.weekdays.includes(day)}
                        onChange={() => toggleWeekday(day)}
                      />
                      {day}
                    </label>
                  ))}
                </div>
              ) : null}
              {scheduleDraft.frequency === 'Monthly' ? (
                <label>
                  Day of month
                  <input
                    type="number"
                    min={1}
                    max={31}
                    required
                    value={scheduleDraft.dayOfMonth}
                    onChange={(event) => setScheduleDraft({ ...scheduleDraft, dayOfMonth: event.target.value })}
                  />
                </label>
              ) : null}
              <label>
                Business impact
                <textarea
                  required
                  maxLength={4000}
                  value={scheduleDraft.businessImpact}
                  onChange={(event) => setScheduleDraft({ ...scheduleDraft, businessImpact: event.target.value })}
                />
              </label>
              <label className="checkbox-row">
                <input
                  type="checkbox"
                  checked={scheduleDraft.ignoreOff}
                  onChange={(event) => setScheduleDraft({ ...scheduleDraft, ignoreOff: event.target.checked })}
                />
                Ignore OFF
              </label>
              {editingScheduleId ? (
                <label className="checkbox-row">
                  <input
                    type="checkbox"
                    checked={scheduleDraft.isActive}
                    onChange={(event) => setScheduleDraft({ ...scheduleDraft, isActive: event.target.checked })}
                  />
                  Active
                </label>
              ) : null}
              <div className="inline-actions">
                <button type="submit" className="button button-primary" disabled={busy}>
                  {editingScheduleId ? 'Save schedule' : 'Create schedule'}
                </button>
                <button type="button" className="button button-ghost" onClick={() => setScheduleDraft(null)}>
                  Cancel
                </button>
              </div>
            </form>
          </div>
        </div>
      ) : null}
    </>
  )
}
