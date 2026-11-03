export type UserRole = 'TenantAdmin' | 'Operator' | 'Approver' | 'PlatformAdmin' | 'Viewer'

export interface UserDto {
  id: string
  tenantId: string
  tenantName: string
  isDemoTenant: boolean
  email: string
  displayName: string
  role: UserRole
}

export interface LoginResponse {
  accessToken: string
  expiresAt: string
  user: UserDto
}

export interface AgentSummary {
  id: string
  hostname: string
  platform: string
  osVersion: string
  architecture: string
  version: string
  status: string
  connectivity: string
  enrolledAt: string
  lastHeartbeatAt: string | null
  lastTelemetryAt: string | null
  collectorHealthy: boolean
  uptimeSeconds: number | null
  cpuPercent: number | null
  memoryPercent: number | null
  diskPercent: number | null
  activeAlerts: number
}

export interface ApplicationCheck {
  id: string; agentId: string; hostname: string; name: string; type: string; target: string
  intervalSeconds: number; enabled: boolean; lastExecutedAt: string | null
  lastDurationMilliseconds: number | null; lastStatus: string; lastErrorCategory: string | null
}
export interface ApplicationCheckHistoryItem {
  eventId: string; status: string; startedAt: string; finishedAt: string
  durationMilliseconds: number; errorCategory: string | null; errorDetail: string | null
}

export interface IncidentAlert { alertId: string; severity: string; status: string; message: string; triggeredAt: string }
export interface Incident {
  id: string; number: string; title: string; description: string; severity: string; status: string
  createdByUserId: string | null; assignedToUserId: string | null; relatedBatchJobExecutionId: string | null
  openedAt: string; acknowledgedAt: string | null; resolvedAt: string | null; closedAt: string | null
  alerts: IncidentAlert[]; relatedChangeRequestIds: string[]
}
export interface ChangeApproval { approverDisplay: string; decision: string; comment: string | null; decidedAt: string }
export interface ChangeRequest {
  id: string; number: string; title: string; description: string; risk: string; targetEnvironment: string
  plannedStartAt: string; plannedEndAt: string; requiredApprovals: number; status: string
  requestedByUserId: string | null; requestedByDisplay: string; relatedIncidentId: string | null; approvals: ChangeApproval[]
}
export interface ChangeApprovalPolicy { requiredApprovals: number; requireSeparationOfDuties: boolean }

export interface ServiceStatus {
  name: string
  kind: string
  state: string
  checkedAt: string
  detail: string | null
}

export interface AgentCredential {
  id: string
  keyPrefix: string
  createdAt: string
  expiresAt: string | null
  revokedAt: string | null
  isExpired: boolean
}

export interface AgentDetail {
  agent: AgentSummary
  services: ServiceStatus[]
  capabilities: string[]
  credentials: AgentCredential[]
  deploymentExecutionAllowed: boolean
}

export interface AgentMetricPoint {
  timestamp: string
  value: number
}

export interface AgentMetrics {
  agentId: string
  range: string
  series: Record<string, AgentMetricPoint[]>
  latest: Record<string, number>
}

export interface EnrollmentCreated {
  enrollmentId: string
  token: string
  tokenPrefix: string
  targetPlatform: { platform: string } | null
  expiresAt: string
  enrollCommand: string
  allowDeploymentExecution: boolean
}

export interface EnrollmentListItem {
  id: string
  tokenPrefix: string
  targetPlatform: string | null
  expiresAt: string
  consumedAt: string | null
  consumedByAgentId: string | null
  isUsable: boolean
  allowDeploymentExecution: boolean
}

export interface RotateCredentialResponse {
  credentialId: string
  apiKey: string
  apiKeyPrefix: string
  createdAt: string
}

export interface RevokeAgentResponse {
  agentId: string
  status: string
  revokedAt: string
}

export interface Alert {
  id: string
  agentId: string
  hostname: string
  alertRuleId: string
  ruleName: string
  metric: string
  severity: string
  status: string
  message: string
  triggeredValue: number
  triggeredAt: string
  acknowledgedAt: string | null
  resolvedAt: string | null
}

export interface AlertRule {
  id: string
  name: string
  metric: string
  operator: string
  threshold: number
  severity: string
  consecutiveBreaches: number
  enabled: boolean
}

export interface AuditEvent {
  id: string
  actorType: string
  actorDisplay: string | null
  action: string
  targetType: string
  targetId: string | null
  outcome: string
  detailsJson: string | null
  ipAddress: string | null
  createdAt: string
}

export interface BatchJobExecution {
  id: string
  correlationId: string
  startedAt: string
  finishedAt: string | null
  status: string
  exitCode: number | null
  failureReason: string | null
  durationSeconds: number | null
  receivedAt: string
}

export interface BatchJob {
  id: string
  jobKey: string
  name: string
  scheduler: string | null
  expectedSchedule: string | null
  agentId: string
  hostname: string
  lastRunAt: string | null
  lastStatus: string | null
  lastExitCode: number | null
  recentExecutions: BatchJobExecution[]
}

export interface ConfigureBatchJobRequest {
  agentId: string; jobKey: string; name: string; scheduler: string | null; expectedSchedule: string | null
}
export interface UpdateBatchJobRequest {
  name: string; scheduler: string | null; expectedSchedule: string | null
}

export interface DeploymentPackage {
  id: string; name: string; version: string; sha256: string; sizeBytes: number; uploadedAt: string
}
export interface DeploymentRequest {
  id: string; targetAgentId: string; targetHostname: string; deploymentPackageId: string; packageName: string
  packageVersion: string; packageSha256: string; packageSizeBytes: number; changeRequestId: string
  changeRequestNumber: string; environment: string; status: string; requiredApprovals: number
  receivedApprovals: number; requestedByUserId: string | null; requestedByDisplay: string; requestedAt: string
}
export interface CreateDeploymentRequest {
  targetAgentId: string; deploymentPackageId: string; changeRequestId: string
}

export interface UsageSummary {
  telemetrySamplesLast30Days: number
  agentHeartbeatsLast30Days: number
  jobEventsLast30Days: number
  generatedAt: string
}

export interface Overview {
  totalAgents: number
  onlineAgents: number
  offlineAgents: number
  activeAlerts: number
  criticalAlerts: number
  activeDeployments: number
  deploymentExecutionEnabled: boolean
  monitoringCoveragePercent: number
  recentAlerts: Alert[]
  recentEvents: AuditEvent[]
  generatedAt: string
}
