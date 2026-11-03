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

public enum IncidentSeverity { Low = 0, Medium = 1, High = 2, Critical = 3 }
public enum IncidentStatus { New = 0, Acknowledged = 1, InProgress = 2, Resolved = 3, Closed = 4 }
public enum ChangeRequestStatus { PendingApproval = 0, Approved = 1, Rejected = 2, Cancelled = 3 }
public enum ChangeApprovalDecision { Approved = 0, Rejected = 1 }
public enum DeploymentRequestStatus { PendingApproval = 0, Approved = 1, Rejected = 2, Cancelled = 3, Queued = 4, Executing = 5, Succeeded = 6, Failed = 7 }

public enum AuditActorType
{
    User = 0,
    Agent = 1,
    System = 2
}
