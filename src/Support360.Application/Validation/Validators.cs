using FluentValidation;
using Support360.Application.Security;
using Support360.Contracts.Agents;
using Support360.Contracts.Applications;
using Support360.Contracts.Auth;
using Support360.Contracts.Dashboard;
using Support360.Contracts.Telemetry;
using Support360.Contracts.Operations;
using Support360.Contracts.Jobs;
using Support360.Contracts.Deployments;
using Support360.Domain;


namespace Support360.Application.Validation;

public sealed class ConfigureBatchJobRequestValidator : AbstractValidator<ConfigureBatchJobRequest>
{
    public ConfigureBatchJobRequestValidator()
    {
        RuleFor(x => x.AgentId).NotEmpty();
        RuleFor(x => x.JobKey).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(255);
        RuleFor(x => x.Scheduler).MaximumLength(255);
        RuleFor(x => x.ExpectedSchedule).MaximumLength(255);
    }
}

public sealed class UpdateBatchJobRequestValidator : AbstractValidator<UpdateBatchJobRequest>
{
    public UpdateBatchJobRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(255);
        RuleFor(x => x.Scheduler).MaximumLength(255);
        RuleFor(x => x.ExpectedSchedule).MaximumLength(255);
    }
}

public sealed class UploadDeploymentPackageRequestValidator : AbstractValidator<UploadDeploymentPackageRequest>
{
    public UploadDeploymentPackageRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Version).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Content).NotNull().Must(content => content is { Length: > 0 and <= DeploymentPackageLimits.MaximumPackageBytes })
            .WithMessage("Package must be between 1 byte and 20 MiB.");
    }
}

public sealed class CreateDeploymentRequestValidator : AbstractValidator<CreateDeploymentRequest>
{
    public CreateDeploymentRequestValidator()
    {
        RuleFor(x => x.TargetAgentId).NotEmpty();
        RuleFor(x => x.DeploymentPackageId).NotEmpty();
        RuleFor(x => x.ChangeRequestId).NotEmpty();
        RuleFor(x => x.GitRepoURL).MaximumLength(2048);
        RuleFor(x => x.GitBranch).MaximumLength(255);
        RuleFor(x => x.TargetPath).MaximumLength(4000);
    }
}

public sealed class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().MaximumLength(320).EmailAddress();
        RuleFor(x => x.Password).NotEmpty().MaximumLength(256);
    }
}

public sealed class CreateEnrollmentRequestValidator : AbstractValidator<CreateEnrollmentRequest>
{
    public CreateEnrollmentRequestValidator()
    {
        RuleFor(x => x.TargetPlatform)
            .Must(v => string.IsNullOrWhiteSpace(v) || Enum.TryParse<AgentPlatform>(v, true, out _))
            .WithMessage("TargetPlatform must be Windows or Linux.");
    }
}

public sealed class EnrollAgentRequestValidator : AbstractValidator<EnrollAgentRequest>
{
    public EnrollAgentRequestValidator()
    {
        RuleFor(x => x.Token).NotEmpty().MaximumLength(256);
        RuleFor(x => x.Hostname).NotEmpty().MaximumLength(255);
        RuleFor(x => x.Platform)
            .Must(v => Enum.TryParse<AgentPlatform>(v, true, out var p) && p != AgentPlatform.Unknown)
            .WithMessage("Platform must be Windows or Linux.");
        RuleFor(x => x.OsVersion).MaximumLength(255);
        RuleFor(x => x.Architecture).MaximumLength(64);
        RuleFor(x => x.AgentVersion).MaximumLength(64);
        RuleFor(x => x.Capabilities).Must(c => c is null || c.Count <= 64);
    }
}

public sealed class HeartbeatRequestValidator : AbstractValidator<HeartbeatRequest>
{
    public HeartbeatRequestValidator()
    {
        RuleFor(x => x.Version).MaximumLength(64);
        RuleFor(x => x.OsVersion).MaximumLength(255);
        RuleFor(x => x.Architecture).MaximumLength(64);
        RuleFor(x => x.CollectorHealth).MaximumLength(2000);
        RuleFor(x => x.Capabilities).Must(c => c is null || c.Count <= 64);
        RuleFor(x => x.Services).Must(s => s is null || s.Count <= 200);
        RuleForEach(x => x.Services).ChildRules(service =>
        {
            service.RuleFor(s => s.Name).NotEmpty().MaximumLength(255);
            service.RuleFor(s => s.Kind).NotEmpty().MaximumLength(32);
            service.RuleFor(s => s.State)
                .Must(v => Enum.TryParse<ServiceRunState>(v, true, out _))
                .WithMessage("Service state must be Running, Stopped, Failed or Unknown.");
            service.RuleFor(s => s.Detail).MaximumLength(2000);
        });
    }
}

public sealed class TelemetryBatchRequestValidator : AbstractValidator<TelemetryBatchRequest>
{
    public TelemetryBatchRequestValidator()
    {
        RuleFor(x => x.Samples).NotNull().Must(s => s!.Count is > 0 and <= 500);
        RuleForEach(x => x.Samples).ChildRules(sample =>
        {
            sample.RuleFor(s => s.EventId).NotEmpty().MaximumLength(100);
            sample.RuleFor(s => s.Metric).NotEmpty().MaximumLength(100);
            sample.RuleFor(s => s.Unit).NotEmpty().MaximumLength(32);
            sample.RuleFor(s => s.Value).Must(double.IsFinite);
            sample.RuleFor(s => s.CapturedAt).NotEmpty();
            sample.RuleFor(s => s.Metric)
                .Must(m => MetricCatalog.Units.ContainsKey(m))
                .WithMessage("Metric is not in the Support360 allowlist.");
        });
    }
}

public sealed class JobEventBatchRequestValidator : AbstractValidator<JobEventBatchRequest>
{
    public JobEventBatchRequestValidator()
    {
        RuleFor(x => x.Events).NotNull().Must(e => e!.Count is > 0 and <= 200);
        RuleForEach(x => x.Events).ChildRules(job =>
        {
            job.RuleFor(j => j.CorrelationId).NotEmpty().MaximumLength(100);
            job.RuleFor(j => j.JobKey).NotEmpty().MaximumLength(200);
            job.RuleFor(j => j.Name).NotEmpty().MaximumLength(255);
            job.RuleFor(j => j.Scheduler).MaximumLength(255);
            job.RuleFor(j => j.FailureReason).MaximumLength(4000);
            job.RuleFor(j => j.Status)
                .Must(v => Enum.TryParse<BatchExecutionStatus>(v, true, out _))
                .WithMessage("Status must be Running, Succeeded, Failed, Skipped or Unknown.");
            job.RuleFor(j => j.FinishedAt)
                .Must((e, finished) => finished is null || finished >= e.StartedAt)
                .WithMessage("FinishedAt must be after StartedAt.");
        });
    }
}

public sealed class CreateApplicationCheckRequestValidator : AbstractValidator<CreateApplicationCheckRequest>
{
    public CreateApplicationCheckRequestValidator()
    {
        Include(new ApplicationCheckFieldsValidator<CreateApplicationCheckRequest>(x => x.Name, x => x.Type, x => x.Target, x => x.IntervalSeconds));
        RuleFor(x => x.AgentId).NotEmpty();
    }
}

public sealed class UpdateApplicationCheckRequestValidator : AbstractValidator<UpdateApplicationCheckRequest>
{
    public UpdateApplicationCheckRequestValidator()
    {
        Include(new ApplicationCheckFieldsValidator<UpdateApplicationCheckRequest>(x => x.Name, x => x.Type, x => x.Target, x => x.IntervalSeconds));
    }
}

internal sealed class ApplicationCheckFieldsValidator<T> : AbstractValidator<T>
{
    public ApplicationCheckFieldsValidator(
        System.Linq.Expressions.Expression<Func<T, string>> name,
        System.Linq.Expressions.Expression<Func<T, string>> type,
        System.Linq.Expressions.Expression<Func<T, string>> target,
        System.Linq.Expressions.Expression<Func<T, int>> interval)
    {
        RuleFor(name).NotEmpty().MaximumLength(200);
        RuleFor(type).Must(v => Enum.TryParse<ApplicationCheckType>(v, true, out var typeValue) && Enum.IsDefined(typeValue))
            .WithMessage("Type must be Http, Process, WindowsService or SystemdService.");
        RuleFor(target).NotEmpty().MaximumLength(2048)
            .Must((model, value) => IsValidTarget(
                type.Compile().Invoke(model), value))
            .WithMessage("Target must be an absolute HTTP(S) URL, a process name without a path, or a valid service name.");
        RuleFor(interval).InclusiveBetween(30, 86400);
    }

    private static bool IsValidTarget(string? typeValue, string? target)
    {
        if (string.IsNullOrWhiteSpace(typeValue) || string.IsNullOrWhiteSpace(target)
            || !Enum.TryParse<ApplicationCheckType>(typeValue, true, out var type) || !Enum.IsDefined(type))
        {
            return false;
        }

        target = target.Trim();
        return type switch
        {
            ApplicationCheckType.Http => Uri.TryCreate(target, UriKind.Absolute, out var uri)
                && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
                && string.IsNullOrEmpty(uri.UserInfo),
            ApplicationCheckType.Process => target.Length <= 255
                && !target.Contains('/') && !target.Contains('\\') && !target.Contains('\0'),
            ApplicationCheckType.WindowsService or ApplicationCheckType.SystemdService =>
                System.Text.RegularExpressions.Regex.IsMatch(target, "^[A-Za-z0-9_.@-]{1,255}$"),
            _ => false
        };
    }
}

public sealed class UpdateHttpCheckTargetPolicyRequestValidator : AbstractValidator<UpdateHttpCheckTargetPolicyRequest>
{
    public UpdateHttpCheckTargetPolicyRequestValidator()
    {
        RuleFor(x => x.AllowedHosts).NotNull()
            .Must(hosts => hosts!.Count <= HttpCheckTargetPolicy.MaxAllowedHosts)
            .WithMessage($"At most {HttpCheckTargetPolicy.MaxAllowedHosts} allowed host patterns may be configured.");
        RuleForEach(x => x.AllowedHosts)
            .Must(HttpCheckTargetPolicy.IsValidPattern)
            .WithMessage("Each entry must be a DNS hostname, a single leading '*.' wildcard, or an IP address.");
    }
}

public sealed class ApplicationCheckResultsRequestValidator : AbstractValidator<ApplicationCheckResultsRequest>
{
    public ApplicationCheckResultsRequestValidator()
    {
        RuleFor(x => x.Results).NotNull().Must(r => r!.Count is > 0 and <= 100);
        RuleForEach(x => x.Results).ChildRules(result =>
        {
            result.RuleFor(x => x.CheckId).NotEmpty();
            result.RuleFor(x => x.EventId).NotEmpty().MaximumLength(100);
            result.RuleFor(x => x.Status)
                .Must(v => Enum.TryParse<ApplicationCheckStatus>(v, true, out var status)
                    && Enum.IsDefined(status) && status != ApplicationCheckStatus.Unknown);
            result.RuleFor(x => x.FinishedAt).GreaterThanOrEqualTo(x => x.StartedAt);
            result.RuleFor(x => x.DurationMilliseconds).InclusiveBetween(0, 120000);
            result.RuleFor(x => x.ErrorCategory).MaximumLength(64);
            result.RuleFor(x => x.ErrorDetail).MaximumLength(1000);
        });
    }
}

public sealed class CreateIncidentRequestValidator : AbstractValidator<CreateIncidentRequest>
{
    public CreateIncidentRequestValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).NotEmpty().MaximumLength(8000);
        RuleFor(x => x.Severity).Must(v => Enum.TryParse<IncidentSeverity>(v, true, out var value) && Enum.IsDefined(value));
        RuleFor(x => x.AlertIds).Must(ids => ids is null || ids.Count <= 50 && ids.Distinct().Count() == ids.Count);
        RuleForEach(x => x.AlertIds).NotEmpty();
    }
}

public sealed class TransitionIncidentRequestValidator : AbstractValidator<TransitionIncidentRequest>
{
    public TransitionIncidentRequestValidator() =>
        RuleFor(x => x.Status).Must(v => Enum.TryParse<IncidentStatus>(v, true, out var value) && Enum.IsDefined(value));
}

public sealed class CreateChangeRequestValidator : AbstractValidator<CreateChangeRequest>
{
    public CreateChangeRequestValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).NotEmpty().MaximumLength(8000);
        RuleFor(x => x.Risk).NotEmpty().MaximumLength(4000);
        RuleFor(x => x.TargetEnvironment).NotEmpty().MaximumLength(80);
        RuleFor(x => x.PlannedStartAt).GreaterThan(DateTimeOffset.UtcNow.AddMinutes(-1));
        RuleFor(x => x.PlannedEndAt).GreaterThan(x => x.PlannedStartAt).LessThanOrEqualTo(x => x.PlannedStartAt.AddDays(7));
    }
}

public sealed class DecideChangeRequestValidator : AbstractValidator<DecideChangeRequest>
{
    public DecideChangeRequestValidator()
    {
        RuleFor(x => x.Decision).Must(v => Enum.TryParse<ChangeApprovalDecision>(v, true, out var value) && Enum.IsDefined(value));
        RuleFor(x => x.Comment).MaximumLength(2000);
    }
}

public sealed class UpdateChangeApprovalPolicyRequestValidator : AbstractValidator<UpdateChangeApprovalPolicyRequest>
{
    public UpdateChangeApprovalPolicyRequestValidator() => RuleFor(x => x.RequiredApprovals).InclusiveBetween(1, 5);
}

public sealed class CreateServiceRequestValidator : AbstractValidator<CreateServiceRequest>
{
    public CreateServiceRequestValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).NotEmpty().MaximumLength(8000);
        RuleFor(x => x.Category).NotEmpty().MaximumLength(100);
    }
}

public sealed class TransitionServiceRequestValidator : AbstractValidator<TransitionServiceRequest>
{
    public TransitionServiceRequestValidator() =>
        RuleFor(x => x.Status).Must(v => Enum.TryParse<ServiceRequestStatus>(v, true, out var value) && Enum.IsDefined(value));
}

public sealed class CreateAlertRuleRequestValidator : AbstractValidator<CreateAlertRuleRequest>
{
    public CreateAlertRuleRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Metric).Must(m => MetricCatalog.Units.ContainsKey(m))
            .WithMessage("Metric is not in the Support360 allowlist.");
        RuleFor(x => x.Operator).Must(v => Enum.TryParse<ComparisonOperator>(v, true, out _));
        RuleFor(x => x.Severity).Must(v => Enum.TryParse<AlertSeverity>(v, true, out _));
        RuleFor(x => x.Threshold).Must(double.IsFinite);
        RuleFor(x => x.ConsecutiveBreaches).InclusiveBetween(1, 100);
    }
}
