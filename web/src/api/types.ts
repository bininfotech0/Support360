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
export interface ServiceRequest {
  id: string; number: string; title: string; description: string; category: string; status: string
  requestedByUserId: string | null; requestedByDisplay: string; assignedToUserId: string | null
  openedAt: string; acknowledgedAt: string | null; resolvedAt: string | null; closedAt: string | null
}
export interface CreateServiceRequest { title: string; description: string; category: string }

export interface TicketAlert { alertId: string; severity: string; status: string; message: string; triggeredAt: string }
export interface TicketApproval { approverDisplay: string; decision: string; comment: string | null; decidedAt: string }
export interface TicketActivity {
  kind: string; fromStatus: string | null; toStatus: string | null; detail: string | null
  actorDisplay: string | null; occurredAt: string
}
export interface TicketLink { ticketId: string; number: string; type: string; linkType: string }
export interface TicketComment { id: string; body: string; isInternal: boolean; authorDisplay: string | null; createdAt: string }
export interface TicketWorklog { id: string; minutes: number; note: string | null; authorDisplay: string | null; workedAt: string }
export interface TicketWatcher { id: string; userId: string | null; display: string; createdAt: string }
export interface TicketAttachment {
  id: string; fileName: string; contentType: string; sizeBytes: number; sha256: string
  uploadedByDisplay: string; uploadedAt: string
}
export interface Ticket {
  id: string; number: string; type: string; title: string; description: string; category: string | null
  status: string; priority: string; impact: string; urgency: string
  requestedByUserId: string | null; requestedByDisplay: string | null
  assignedToUserId: string | null; relatedBatchJobExecutionId: string | null
  risk: string | null; targetEnvironment: string | null
  plannedStartAt: string | null; plannedEndAt: string | null; requiredApprovals: number
  openedAt: string; acknowledgedAt: string | null; resolvedAt: string | null; closedAt: string | null
  alerts: TicketAlert[]; approvals: TicketApproval[]; activities: TicketActivity[]; relatedTickets: TicketLink[]
  comments: TicketComment[]; worklogs: TicketWorklog[]; watchers: TicketWatcher[]; attachments: TicketAttachment[]
  dueAt: string | null
}
export interface CreateTicket {
  type: string; title: string; description: string; category?: string | null
  priority?: string | null; impact?: string | null; urgency?: string | null
  assignedToUserId?: string | null; alertIds?: string[] | null; relatedBatchJobExecutionId?: string | null
  risk?: string | null; targetEnvironment?: string | null
  plannedStartAt?: string | null; plannedEndAt?: string | null; relatedTicketId?: string | null
}

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
  changeRequestNumber: string; environment: string; gitRepoUrl: string | null; gitBranch: string | null
  targetPath: string | null; status: string; requiredApprovals: number
  receivedApprovals: number; requestedByUserId: string | null; requestedByDisplay: string; requestedAt: string
}
export interface CreateDeploymentRequest {
  targetAgentId: string; deploymentPackageId: string; changeRequestId: string
  gitRepoUrl?: string | null; gitBranch?: string | null; targetPath?: string | null
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

export interface Project {
  id: string
  name: string
  status: string
}

export interface WorkDnaCategory {
  id: string
  categoryName: string
  activityName: string
}

export interface WorkDnaTeamMember {
  id: string
  displayName: string
  email: string
  role: string
}

export interface WorkDnaLookups {
  organizationName: string
  projects: Project[]
  categories: WorkDnaCategory[]
  team: WorkDnaTeamMember[]
}

export interface WorkActivity {
  id: string
  organization: string
  projectId: string
  projectName: string
  assigneeId: string
  assigneeName: string
  dnaCategoryId: string
  categoryName: string
  activityName: string
  title: string
  description: string | null
  targetMode: string
  effortMinutes: number
  effort: string
  severity: string
  startTime: string
  endTime: string
  ignoreOff: boolean
  businessImpact: string | null
  status: string
  completedAt: string | null
  scheduleId: string | null
  isSuppressed: boolean
}

export interface WorkCalendarDay {
  date: string
  isOff: boolean
  offLabel: string | null
  total: number
  completed: number
  pending: number
  scheduled: number
  suppressed: number
}

export interface WorkPulsePoint {
  date: string
  bookedHours: number
  capacityHours: number
  pending: number
}

export interface WorkCapacity {
  month: string
  totalWorkingDays: number
  holidayDays: number
  leaveDays: number
  workingCapacityHours: number
  bookedHours: number
  availableHours: number
  utilizationPercent: number
  pendingActivities: number
  days: WorkCalendarDay[]
  pendencyTimeline: WorkPulsePoint[]
}

export interface WorkCapacityRange {
  from: string
  to: string
  months: WorkCapacity[]
}

export interface AutoCaptureWorkActivityRequest {
  projectId: string
  assigneeId: string
  dnaCategoryId: string
  title: string
  description: string | null
  effortMinutes: number
  severity: string
  businessImpact: string
  source: string
  sourceReference: string | null
  startTime: string | null
}

export interface CreateWorkActivityRequest {
  projectId: string
  assigneeId: string
  dnaCategoryId: string
  title: string
  description: string | null
  targetMode: string
  effortMinutes: number
  severity: string
  startTime: string
  endTime: string
  ignoreOff: boolean
  businessImpact: string
}

export interface UpdateWorkActivityRequest extends CreateWorkActivityRequest {
  status?: string
}

export interface WorkActivitySchedule {
  id: string
  projectId: string
  projectName: string
  assigneeId: string
  assigneeName: string
  dnaCategoryId: string
  categoryName: string
  activityName: string
  title: string
  description: string | null
  targetMode: string
  effortMinutes: number
  effort: string
  severity: string
  ignoreOff: boolean
  businessImpact: string | null
  frequency: string
  weekdays: string[]
  dayOfMonth: number | null
  startHourUtc: number
  startDate: string
  endDate: string
  isActive: boolean
}

export interface CreateWorkActivityScheduleRequest {
  projectId: string
  assigneeId: string
  dnaCategoryId: string
  title: string
  description: string | null
  targetMode: string
  effortMinutes: number
  severity: string
  ignoreOff: boolean
  businessImpact: string
  frequency: string
  weekdays: string[]
  dayOfMonth: number | null
  startHourUtc: number
  startDate: string
  endDate: string
}

export interface UpdateWorkActivityScheduleRequest extends CreateWorkActivityScheduleRequest {
  isActive: boolean
}

export interface MaterializeSchedulesResponse {
  month: string
  generated: number
  skipped: number
}

export interface WorkCalendarDayEntry {
  date: string
  dayType: string
  label: string | null
}

export interface WorkLeave {
  id: string
  userId: string
  userDisplayName: string
  date: string
  hours: number
  reason: string | null
}
