import { clearSession, getSession } from './session'
import type {
  AgentDetail,
  AgentMetrics,
  AgentSummary,
  ApplicationCheck,
  ApplicationCheckHistoryItem,
  ChangeApprovalPolicy,
  ChangeRequest,
  Incident,
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
  UsageSummary,
  UserDto,
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

  incidents(status?: string): Promise<Incident[]> {
    const query = status ? `?status=${encodeURIComponent(status)}` : ''
    return request<Incident[]>(`/api/incidents${query}`)
  },

  createIncident(incident: { title: string; description: string; severity: string; alertIds: string[]; relatedBatchJobExecutionId: string | null }): Promise<Incident> {
    return request<Incident>('/api/incidents', { method: 'POST', body: JSON.stringify(incident) })
  },

  transitionIncident(id: string, status: string, assignedToUserId: string | null = null): Promise<Incident> {
    return request<Incident>(`/api/incidents/${id}/status`, { method: 'PUT', body: JSON.stringify({ status, assignedToUserId }) })
  },

  changeRequests(): Promise<ChangeRequest[]> {
    return request<ChangeRequest[]>('/api/change-requests')
  },

  createChangeRequest(change: { title: string; description: string; risk: string; targetEnvironment: string; plannedStartAt: string; plannedEndAt: string; relatedIncidentId: string | null }): Promise<ChangeRequest> {
    return request<ChangeRequest>('/api/change-requests', { method: 'POST', body: JSON.stringify(change) })
  },

  decideChangeRequest(id: string, decision: 'Approved' | 'Rejected', comment: string): Promise<ChangeRequest> {
    return request<ChangeRequest>(`/api/change-requests/${id}/decision`, { method: 'POST', body: JSON.stringify({ decision, comment }) })
  },

  cancelChangeRequest(id: string): Promise<ChangeRequest> {
    return request<ChangeRequest>(`/api/change-requests/${id}/cancel`, { method: 'POST' })
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

  usage(): Promise<UsageSummary> {
    return request<UsageSummary>('/api/usage')
  },
}

export function realtimeUrl(): string {
  const base = API_BASE || window.location.origin
  return `${base}/hubs/dashboard`
}
