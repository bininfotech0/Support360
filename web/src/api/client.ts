import { clearSession, getSession } from './session'
import type {
  AgentDetail,
  AgentMetrics,
  AgentSummary,
  ApplicationCheck,
  ApplicationCheckHistoryItem,
  ChangeApprovalPolicy,
  ChangeRequest,
  CreateServiceRequest,
  CreateTicket,
  Incident,
  Ticket,
  Alert,
  AlertRule,
  AuditEvent,
  BatchJob,
  ConfigureBatchJobRequest,
  UpdateBatchJobRequest,
  DeploymentPackage,
  DeploymentRequest,
  CreateDeploymentRequest,
  EnrollmentCreated,
  EnrollmentListItem,
  LoginResponse,
  Overview,
  RevokeAgentResponse,
  RotateCredentialResponse,
  ServiceRequest,
  UsageSummary,
  UserDto,
  WorkActivity,
  WorkActivitySchedule,
  WorkCalendarDayEntry,
  WorkCapacity,
  WorkCapacityRange,
  AutoCaptureWorkActivityRequest,
  WorkDnaCategory,
  WorkDnaLookups,
  WorkLeave,
  CreateWorkActivityRequest,
  UpdateWorkActivityRequest,
  CreateWorkActivityScheduleRequest,
  UpdateWorkActivityScheduleRequest,
  MaterializeSchedulesResponse,
  Project,
} from './types'

const API_BASE = (import.meta.env.VITE_API_BASE_URL as string | undefined)?.replace(/\/$/, '') ?? ''

export class ApiError extends Error {
  readonly status: number

  constructor(status: number, message: string) {
    super(message)
    this.name = 'ApiError'
    this.status = status
  }
}

async function request<T>(path: string, init: RequestInit = {}): Promise<T> {
  const session = getSession()
  const headers = new Headers(init.headers)
  if (init.body && !(init.body instanceof FormData) && !headers.has('Content-Type')) {
    headers.set('Content-Type', 'application/json')
  }
  if (session) {
    headers.set('Authorization', `Bearer ${session.token}`)
  }

  const response = await fetch(`${API_BASE}${path}`, {
    ...init,
    headers,
    cache: 'no-store',
  })

  if (response.status === 401 && session) {
    clearSession()
    window.dispatchEvent(new Event('support360:unauthorized'))
  }

  if (!response.ok) {
    let message = `Request failed with status ${response.status}.`
    try {
      const problem = (await response.json()) as {
        title?: string
        detail?: string
        errors?: Record<string, string[]>
      }
      const fieldErrors = problem.errors
        ? Object.values(problem.errors).flat().join(' ')
        : ''
      message = [problem.detail ?? problem.title, fieldErrors].filter(Boolean).join(' ') || message
    } catch {
      // Keep the status-based message when the response is not JSON.
    }
    throw new ApiError(response.status, message)
  }

  if (response.status === 204) {
    return undefined as T
  }

  return (await response.json()) as T
}

async function requestBlob(path: string): Promise<Blob> {
  const session = getSession()
  const headers = new Headers()
  if (session) {
    headers.set('Authorization', `Bearer ${session.token}`)
  }

  const response = await fetch(`${API_BASE}${path}`, { headers, cache: 'no-store' })

  if (response.status === 401 && session) {
    clearSession()
    window.dispatchEvent(new Event('support360:unauthorized'))
  }

  if (!response.ok) {
    let message = `Request failed with status ${response.status}.`
    try {
      const problem = (await response.json()) as { title?: string; detail?: string }
      message = problem.detail ?? problem.title ?? message
    } catch {
      // Keep the status-based message when the response is not JSON.
    }
    throw new ApiError(response.status, message)
  }

  return response.blob()
}

function toIncident(ticket: Ticket): Incident {
  return {
    id: ticket.id,
    number: ticket.number,
    title: ticket.title,
    description: ticket.description,
    severity: ticket.priority,
    status: ticket.status,
    createdByUserId: ticket.requestedByUserId,
    assignedToUserId: ticket.assignedToUserId,
    relatedBatchJobExecutionId: ticket.relatedBatchJobExecutionId,
    openedAt: ticket.openedAt,
    acknowledgedAt: ticket.acknowledgedAt,
    resolvedAt: ticket.resolvedAt,
    closedAt: ticket.closedAt,
    alerts: ticket.alerts,
    relatedChangeRequestIds: ticket.relatedTickets.filter((link) => link.type === 'Change').map((link) => link.ticketId),
  }
}

function toChangeRequest(ticket: Ticket): ChangeRequest {
  return {
    id: ticket.id,
    number: ticket.number,
    title: ticket.title,
    description: ticket.description,
    risk: ticket.risk ?? '',
    targetEnvironment: ticket.targetEnvironment ?? '',
    plannedStartAt: ticket.plannedStartAt ?? '',
    plannedEndAt: ticket.plannedEndAt ?? '',
    requiredApprovals: ticket.requiredApprovals,
    status: ticket.status,
    requestedByUserId: ticket.requestedByUserId,
    requestedByDisplay: ticket.requestedByDisplay ?? '',
    relatedIncidentId: ticket.relatedTickets.find((link) => link.type === 'Incident')?.ticketId ?? null,
    approvals: ticket.approvals,
  }
}

function toServiceRequest(ticket: Ticket): ServiceRequest {
  return {
    id: ticket.id,
    number: ticket.number,
    title: ticket.title,
    description: ticket.description,
    category: ticket.category ?? '',
    status: ticket.status,
    requestedByUserId: ticket.requestedByUserId,
    requestedByDisplay: ticket.requestedByDisplay ?? '',
    assignedToUserId: ticket.assignedToUserId,
    openedAt: ticket.openedAt,
    acknowledgedAt: ticket.acknowledgedAt,
    resolvedAt: ticket.resolvedAt,
    closedAt: ticket.closedAt,
  }
}

function ticketQuery(type: string, status?: string): string {
  const params = new URLSearchParams({ type })
  if (status) {
    params.set('status', status)
  }
  return params.toString()
}

function createTicket(body: CreateTicket): Promise<Ticket> {
  return request<Ticket>('/api/tickets', { method: 'POST', body: JSON.stringify(body) })
}

function transitionTicket(id: string, status: string, assignedToUserId: string | null = null): Promise<Ticket> {
  return request<Ticket>(`/api/tickets/${id}/status`, {
    method: 'PUT',
    body: JSON.stringify({ status, assignedToUserId, reason: null }),
  })
}

export const api = {
  login(email: string, password: string): Promise<LoginResponse> {
    return request<LoginResponse>('/api/auth/login', {
      method: 'POST',
      body: JSON.stringify({ email, password }),
    })
  },

  me(): Promise<{ user: UserDto }> {
    return request<{ user: UserDto }>('/api/auth/me')
  },

  overview(): Promise<Overview> {
    return request<Overview>('/api/overview')
  },

  agents(): Promise<AgentSummary[]> {
    return request<AgentSummary[]>('/api/agents')
  },

  ticket(id: string): Promise<Ticket> {
    return request<Ticket>(`/api/tickets/${id}`)
  },

  addTicketComment(id: string, body: string, isInternal: boolean): Promise<Ticket> {
    return request<Ticket>(`/api/tickets/${id}/comments`, {
      method: 'POST',
      body: JSON.stringify({ body, isInternal }),
    })
  },

  addTicketWorklog(id: string, minutes: number, note: string | null): Promise<Ticket> {
    return request<Ticket>(`/api/tickets/${id}/worklogs`, {
      method: 'POST',
      body: JSON.stringify({ minutes, note }),
    })
  },

  setTicketDueDate(id: string, dueAt: string | null): Promise<Ticket> {
    return request<Ticket>(`/api/tickets/${id}/due-date`, { method: 'PUT', body: JSON.stringify({ dueAt }) })
  },

  setTicketWatching(id: string, watch: boolean): Promise<Ticket> {
    return request<Ticket>(`/api/tickets/${id}/watch`, { method: watch ? 'POST' : 'DELETE' })
  },

  uploadTicketAttachment(id: string, file: File): Promise<Ticket> {
    const form = new FormData()
    form.set('file', file)
    return request<Ticket>(`/api/tickets/${id}/attachments`, { method: 'POST', body: form })
  },

  downloadTicketAttachment(id: string, attachmentId: string): Promise<Blob> {
    return requestBlob(`/api/tickets/${id}/attachments/${attachmentId}`)
  },

  deleteTicketAttachment(id: string, attachmentId: string): Promise<Ticket> {
    return request<Ticket>(`/api/tickets/${id}/attachments/${attachmentId}`, { method: 'DELETE' })
  },

  async incidents(status?: string): Promise<Incident[]> {
    const tickets = await request<Ticket[]>(`/api/tickets?${ticketQuery('incident', status)}`)
    return tickets.map(toIncident)
  },

  async createIncident(incident: { title: string; description: string; severity: string; alertIds: string[]; relatedBatchJobExecutionId: string | null }): Promise<Incident> {
    return toIncident(await createTicket({
      type: 'Incident',
      title: incident.title,
      description: incident.description,
      priority: incident.severity,
      alertIds: incident.alertIds,
      relatedBatchJobExecutionId: incident.relatedBatchJobExecutionId,
    }))
  },

  async transitionIncident(id: string, status: string, assignedToUserId: string | null = null): Promise<Incident> {
    return toIncident(await transitionTicket(id, status, assignedToUserId))
  },

  async changeRequests(): Promise<ChangeRequest[]> {
    const tickets = await request<Ticket[]>(`/api/tickets?${ticketQuery('change')}`)
    return tickets.map(toChangeRequest)
  },

  async createChangeRequest(change: { title: string; description: string; risk: string; targetEnvironment: string; plannedStartAt: string; plannedEndAt: string; relatedIncidentId: string | null }): Promise<ChangeRequest> {
    return toChangeRequest(await createTicket({
      type: 'Change',
      title: change.title,
      description: change.description,
      risk: change.risk,
      targetEnvironment: change.targetEnvironment,
      plannedStartAt: change.plannedStartAt,
      plannedEndAt: change.plannedEndAt,
      relatedTicketId: change.relatedIncidentId,
    }))
  },

  async decideChangeRequest(id: string, decision: 'Approved' | 'Rejected', comment: string): Promise<ChangeRequest> {
    const ticket = await request<Ticket>(`/api/tickets/${id}/decision`, {
      method: 'POST',
      body: JSON.stringify({ decision, comment }),
    })
    return toChangeRequest(ticket)
  },

  async cancelChangeRequest(id: string): Promise<ChangeRequest> {
    return toChangeRequest(await request<Ticket>(`/api/tickets/${id}/cancel`, { method: 'POST' }))
  },

  async serviceRequests(status?: string): Promise<ServiceRequest[]> {
    const tickets = await request<Ticket[]>(`/api/tickets?${ticketQuery('serviceRequest', status)}`)
    return tickets.map(toServiceRequest)
  },

  async createServiceRequest(serviceRequest: CreateServiceRequest): Promise<ServiceRequest> {
    return toServiceRequest(await createTicket({
      type: 'ServiceRequest',
      title: serviceRequest.title,
      description: serviceRequest.description,
      category: serviceRequest.category,
    }))
  },

  async transitionServiceRequest(id: string, status: string): Promise<ServiceRequest> {
    return toServiceRequest(await transitionTicket(id, status))
  },

  changeApprovalPolicy(): Promise<ChangeApprovalPolicy> {
    return request<ChangeApprovalPolicy>('/api/change-management/policy')
  },

  updateChangeApprovalPolicy(policy: ChangeApprovalPolicy): Promise<ChangeApprovalPolicy> {
    return request<ChangeApprovalPolicy>('/api/change-management/policy', { method: 'PUT', body: JSON.stringify(policy) })
  },

  applicationChecks(): Promise<ApplicationCheck[]> {
    return request<ApplicationCheck[]>('/api/application-checks')
  },

  createApplicationCheck(check: Omit<ApplicationCheck, 'id' | 'hostname' | 'lastExecutedAt' | 'lastDurationMilliseconds' | 'lastStatus' | 'lastErrorCategory'>): Promise<ApplicationCheck> {
    return request<ApplicationCheck>('/api/application-checks', { method: 'POST', body: JSON.stringify(check) })
  },

  deleteApplicationCheck(id: string): Promise<void> {
    return request<void>(`/api/application-checks/${id}`, { method: 'DELETE' })
  },

  updateApplicationCheck(id: string, check: { name: string; type: string; target: string; intervalSeconds: number; enabled: boolean }): Promise<ApplicationCheck> {
    return request<ApplicationCheck>(`/api/application-checks/${id}`, { method: 'PUT', body: JSON.stringify(check) })
  },

  applicationCheckHistory(id: string): Promise<ApplicationCheckHistoryItem[]> {
    return request<ApplicationCheckHistoryItem[]>(`/api/application-checks/${id}/history`)
  },

  agent(id: string): Promise<AgentDetail> {
    return request<AgentDetail>(`/api/agents/${id}`)
  },

  metrics(id: string, range: string): Promise<AgentMetrics> {
    return request<AgentMetrics>(`/api/agents/${id}/metrics?range=${encodeURIComponent(range)}`)
  },

  rotateCredential(id: string): Promise<RotateCredentialResponse> {
    return request<RotateCredentialResponse>(`/api/agents/${id}/rotate-credential`, { method: 'POST' })
  },

  revokeAgent(id: string): Promise<RevokeAgentResponse> {
    return request<RevokeAgentResponse>(`/api/agents/${id}/revoke`, { method: 'POST' })
  },

  enrollments(): Promise<EnrollmentListItem[]> {
    return request<EnrollmentListItem[]>('/api/enrollments')
  },

  createEnrollment(targetPlatform: string | null, allowDeploymentExecution = false): Promise<EnrollmentCreated> {
    return request<EnrollmentCreated>('/api/enrollments', {
      method: 'POST',
      body: JSON.stringify({ targetPlatform, allowDeploymentExecution }),
    })
  },

  alerts(status?: string): Promise<Alert[]> {
    const query = status ? `?status=${encodeURIComponent(status)}` : ''
    return request<Alert[]>(`/api/alerts${query}`)
  },

  acknowledgeAlert(id: string): Promise<void> {
    return request<void>(`/api/alerts/${id}/acknowledge`, { method: 'POST' })
  },

  alertRules(): Promise<AlertRule[]> {
    return request<AlertRule[]>('/api/alert-rules')
  },

  createAlertRule(rule: Omit<AlertRule, 'id' | 'enabled'>): Promise<AlertRule> {
    return request<AlertRule>('/api/alert-rules', {
      method: 'POST',
      body: JSON.stringify(rule),
    })
  },

  audit(take = 100): Promise<AuditEvent[]> {
    return request<AuditEvent[]>(`/api/audit?take=${take}`)
  },

  batchJobs(): Promise<BatchJob[]> {
    return request<BatchJob[]>('/api/batch-jobs')
  },

  configureBatchJob(job: ConfigureBatchJobRequest): Promise<BatchJob> {
    return request<BatchJob>('/api/batch-jobs', { method: 'POST', body: JSON.stringify(job) })
  },

  updateBatchJob(id: string, job: UpdateBatchJobRequest): Promise<BatchJob> {
    return request<BatchJob>(`/api/batch-jobs/${id}`, { method: 'PUT', body: JSON.stringify(job) })
  },

  deploymentPackages(): Promise<DeploymentPackage[]> {
    return request<DeploymentPackage[]>('/api/deployment-packages')
  },

  uploadDeploymentPackage(name: string, version: string, file: File): Promise<DeploymentPackage> {
    const form = new FormData()
    form.set('name', name); form.set('version', version); form.set('file', file)
    return request<DeploymentPackage>('/api/deployment-packages', { method: 'POST', body: form })
  },

  deployments(): Promise<DeploymentRequest[]> {
    return request<DeploymentRequest[]>('/api/deployments')
  },

  createDeployment(deployment: CreateDeploymentRequest): Promise<DeploymentRequest> {
    return request<DeploymentRequest>('/api/deployments', { method: 'POST', body: JSON.stringify(deployment) })
  },

  workDnaLookups(): Promise<WorkDnaLookups> {
    return request<WorkDnaLookups>('/api/work-dna/lookups')
  },

  workActivities(month: string, filters: { assigneeId?: string; status?: string; projectId?: string } = {}): Promise<WorkActivity[]> {
    const params = new URLSearchParams({ month })
    if (filters.assigneeId) params.set('assigneeId', filters.assigneeId)
    if (filters.status) params.set('status', filters.status)
    if (filters.projectId) params.set('projectId', filters.projectId)
    return request<WorkActivity[]>(`/api/work-dna/activities?${params.toString()}`)
  },

  createWorkActivity(activity: CreateWorkActivityRequest): Promise<WorkActivity> {
    return request<WorkActivity>('/api/work-dna/activities', { method: 'POST', body: JSON.stringify(activity) })
  },

  autoCaptureWorkActivity(activity: AutoCaptureWorkActivityRequest): Promise<WorkActivity> {
    return request<WorkActivity>('/api/work-dna/activities/auto-capture', {
      method: 'POST',
      body: JSON.stringify(activity),
    })
  },

  updateWorkActivity(id: string, activity: UpdateWorkActivityRequest): Promise<WorkActivity> {
    return request<WorkActivity>(`/api/work-dna/activities/${id}`, { method: 'PUT', body: JSON.stringify(activity) })
  },

  completeWorkActivity(id: string): Promise<WorkActivity> {
    return request<WorkActivity>(`/api/work-dna/activities/${id}/complete`, { method: 'POST' })
  },

  workCapacity(month: string): Promise<WorkCapacity> {
    return request<WorkCapacity>(`/api/work-dna/capacity?month=${encodeURIComponent(month)}`)
  },

  workCapacityRange(from: string, to: string): Promise<WorkCapacityRange> {
    return request<WorkCapacityRange>(
      `/api/work-dna/capacity/range?from=${encodeURIComponent(from)}&to=${encodeURIComponent(to)}`,
    )
  },

  workSchedules(): Promise<WorkActivitySchedule[]> {
    return request<WorkActivitySchedule[]>('/api/work-dna/schedules')
  },

  createWorkSchedule(schedule: CreateWorkActivityScheduleRequest): Promise<WorkActivitySchedule> {
    return request<WorkActivitySchedule>('/api/work-dna/schedules', { method: 'POST', body: JSON.stringify(schedule) })
  },

  updateWorkSchedule(id: string, schedule: UpdateWorkActivityScheduleRequest): Promise<WorkActivitySchedule> {
    return request<WorkActivitySchedule>(`/api/work-dna/schedules/${id}`, { method: 'PUT', body: JSON.stringify(schedule) })
  },

  materializeWorkSchedules(month: string): Promise<MaterializeSchedulesResponse> {
    return request<MaterializeSchedulesResponse>(`/api/work-dna/schedules/materialize?month=${encodeURIComponent(month)}`, { method: 'POST' })
  },

  materializeWorkSchedule(id: string, month: string): Promise<MaterializeSchedulesResponse> {
    return request<MaterializeSchedulesResponse>(`/api/work-dna/schedules/${id}/materialize?month=${encodeURIComponent(month)}`, { method: 'POST' })
  },

  workCalendarDays(month: string): Promise<WorkCalendarDayEntry[]> {
    return request<WorkCalendarDayEntry[]>(`/api/work-dna/calendar-days?month=${encodeURIComponent(month)}`)
  },

  setWorkCalendarDay(day: { date: string; dayType: string; label: string | null }): Promise<WorkCalendarDayEntry> {
    return request<WorkCalendarDayEntry>('/api/work-dna/calendar-days', { method: 'POST', body: JSON.stringify(day) })
  },

  removeWorkCalendarDay(date: string): Promise<void> {
    return request<void>(`/api/work-dna/calendar-days/${encodeURIComponent(date)}`, { method: 'DELETE' })
  },

  workLeaves(month: string): Promise<WorkLeave[]> {
    return request<WorkLeave[]>(`/api/work-dna/leaves?month=${encodeURIComponent(month)}`)
  },

  createWorkLeave(leave: { userId: string; date: string; hours: number; reason: string | null }): Promise<WorkLeave> {
    return request<WorkLeave>('/api/work-dna/leaves', { method: 'POST', body: JSON.stringify(leave) })
  },

  projects(): Promise<Project[]> {
    return request<Project[]>('/api/projects')
  },

  createProject(name: string): Promise<Project> {
    return request<Project>('/api/projects', { method: 'POST', body: JSON.stringify({ name }) })
  },

  createWorkDnaCategory(category: { categoryName: string; activityName: string }): Promise<WorkDnaCategory> {
    return request<WorkDnaCategory>('/api/work-dna/categories', { method: 'POST', body: JSON.stringify(category) })
  },

  usage(): Promise<UsageSummary> {
    return request<UsageSummary>('/api/usage')
  },
}

export function realtimeUrl(): string {
  const base = API_BASE || window.location.origin
  return `${base}/hubs/dashboard`
}
