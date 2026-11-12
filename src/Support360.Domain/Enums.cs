namespace Support360.Domain;

public enum TenantStatus
{
    Active = 0,
    Suspended = 1
}

public enum UserRole
{
    Viewer = 0,
    Operator = 1,
    Approver = 2,
    TenantAdmin = 3,
    PlatformAdmin = 4
}

public enum AgentPlatform
{
    Unknown = 0,
    Windows = 1,
    Linux = 2
}

public enum AgentStatus
{
    Active = 0,
    Disabled = 1,
    Revoked = 2
}

public enum AgentConnectivity
{
    Unknown = 0,
    Online = 1,
    Offline = 2
}

public enum ServiceRunState
{
    Unknown = 0,
    Running = 1,
    Stopped = 2,
    Failed = 3
}

public enum AlertSeverity
{
    Info = 0,
    Warning = 1,
    Critical = 2
}

public enum AlertStatus
{
    Active = 0,
    Acknowledged = 1,
    Resolved = 2
}

public enum ComparisonOperator
{
    GreaterThan = 0,
    GreaterThanOrEqual = 1,
    LessThan = 2,
    LessThanOrEqual = 3
}

public enum BatchExecutionStatus
{
    Running = 0,
    Succeeded = 1,
    Failed = 2,
    Skipped = 3,
    Unknown = 4
}

public enum ApplicationCheckType
{
    Http = 0,
    Process = 1,
    WindowsService = 2,
    SystemdService = 3
}

public enum ApplicationCheckStatus
{
    Unknown = 0,
    Healthy = 1,
    Unhealthy = 2,
    Unsupported = 3,
    Present = 4
}

public enum TicketType { Incident = 0, ServiceRequest = 1, Problem = 2, Change = 3 }
public enum TicketStatus
{
    New = 0,
    Acknowledged = 1,
    InProgress = 2,
    PendingApproval = 3,
    Approved = 4,
    Rejected = 5,
    Cancelled = 6,
    Resolved = 7,
    Closed = 8
}
public enum TicketPriority { Low = 0, Medium = 1, High = 2, Critical = 3 }
public enum TicketImpact { Low = 0, Medium = 1, High = 2 }
public enum TicketUrgency { Low = 0, Medium = 1, High = 2 }
public enum TicketLinkType { RelatesTo = 0, CausedBy = 1, ResolvedBy = 2 }
public enum TicketApprovalDecision { Approved = 0, Rejected = 1 }
public enum DeploymentRequestStatus { PendingApproval = 0, Approved = 1, Rejected = 2, Cancelled = 3, Queued = 4, Executing = 5, Succeeded = 6, Failed = 7 }

public enum AuditActorType
{
    User = 0,
    Agent = 1,
    System = 2
}

public enum WorkDnaTargetMode { Auto = 0, Manual = 1 }
public enum WorkDnaSeverity { Low = 0, Medium = 1, High = 2, Critical = 3 }
public enum WorkActivityStatus { Pending = 0, Scheduled = 1, Completed = 2, Overdue = 3 }
public enum WorkScheduleFrequency { Daily = 0, Weekly = 1, Monthly = 2 }
public enum WorkCalendarDayType { Working = 0, Holiday = 1, Off = 2 }
