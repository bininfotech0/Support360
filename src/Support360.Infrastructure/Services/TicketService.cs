using System.Security.Cryptography;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Support360.Application.Abstractions;
using Support360.Application.Exceptions;
using Support360.Contracts.Operations;
using Support360.Domain;
using Support360.Domain.Entities;

namespace Support360.Infrastructure.Services;

public sealed class TicketService : ITicketService
{
    private const int MaxListResults = 200;

    private readonly ISupport360DbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IClock _clock;
    private readonly IAuditService _audit;
    private readonly IRealtimeNotifier _realtime;
    private readonly IValidator<CreateTicketRequest> _createTicket;
    private readonly IValidator<TransitionTicketRequest> _transitionTicket;
    private readonly IValidator<DecideTicketRequest> _decideTicket;
    private readonly IValidator<AddTicketCommentRequest> _addComment;
    private readonly IValidator<AddTicketWorklogRequest> _addWorklog;
    private readonly IValidator<SetTicketDueDateRequest> _setDueDate;
    private readonly IValidator<UpdateChangeApprovalPolicyRequest> _policyValidator;

    public TicketService(
        ISupport360DbContext db,
        ITenantContext tenant,
        IClock clock,
        IAuditService audit,
        IRealtimeNotifier realtime,
        IValidator<CreateTicketRequest> createTicket,
        IValidator<TransitionTicketRequest> transitionTicket,
        IValidator<DecideTicketRequest> decideTicket,
        IValidator<AddTicketCommentRequest> addComment,
        IValidator<AddTicketWorklogRequest> addWorklog,
        IValidator<SetTicketDueDateRequest> setDueDate,
        IValidator<UpdateChangeApprovalPolicyRequest> policyValidator)
    {
        _db = db; _tenant = tenant; _clock = clock; _audit = audit; _realtime = realtime;
        _createTicket = createTicket; _transitionTicket = transitionTicket;
        _decideTicket = decideTicket; _addComment = addComment; _addWorklog = addWorklog;
        _setDueDate = setDueDate; _policyValidator = policyValidator;
    }

    public async Task<IReadOnlyList<TicketDto>> ListAsync(Guid tenantId, string? type, string? status, string? priority, Guid? assignedToUserId, string? search, CancellationToken cancellationToken)
    {
        _tenant.SetTenant(tenantId);
        var query = _db.Tickets.AsNoTracking().Include(t => t.Alerts).ThenInclude(a => a.Alert).Include(t => t.Approvals)
            .Where(t => t.TenantId == tenantId);

        if (!string.IsNullOrWhiteSpace(type))
        {
            var parsedType = ParseEnum<TicketType>(type, "ticket type");
            query = query.Where(t => t.Type == parsedType);
        }
        if (!string.IsNullOrWhiteSpace(status))
        {
            var parsedStatus = ParseEnum<TicketStatus>(status, "ticket status");
            query = query.Where(t => t.Status == parsedStatus);
        }
        if (!string.IsNullOrWhiteSpace(priority))
        {
            var parsedPriority = ParseEnum<TicketPriority>(priority, "ticket priority");
            query = query.Where(t => t.Priority == parsedPriority);
        }
        if (assignedToUserId is Guid assignee)
        {
            query = query.Where(t => t.AssignedToUserId == assignee);
        }
        if (!string.IsNullOrWhiteSpace(search))
        {
            var needle = search.Trim().ToLowerInvariant();
            query = query.Where(t => t.Number.ToLower().Contains(needle) || t.Title.ToLower().Contains(needle));
        }

        var tickets = await query.OrderByDescending(t => t.OpenedAt).Take(MaxListResults).ToListAsync(cancellationToken);
        return tickets.Select(t => MapSummary(t)).ToArray();
    }

    public async Task<TicketDto?> GetAsync(RequestActor actor, Guid ticketId, CancellationToken cancellationToken)
    {
        _tenant.SetTenant(actor.TenantId);
        var ticket = await _db.Tickets.AsNoTracking()
            .Include(t => t.Alerts).ThenInclude(a => a.Alert)
            .Include(t => t.Approvals)
            .Include(t => t.Activities)
            .SingleOrDefaultAsync(t => t.TenantId == actor.TenantId && t.Id == ticketId, cancellationToken);
        if (ticket is null) return null;
        return await MapDetailAsync(ticket, CanViewInternal(actor), cancellationToken);
    }

    public async Task<TicketDto> CreateAsync(RequestActor actor, CreateTicketRequest request, CancellationToken cancellationToken)
    {
        await _createTicket.ValidateAndThrowAsync(request, cancellationToken);
        _tenant.SetTenant(actor.TenantId);
        var type = Enum.Parse<TicketType>(request.Type, true);
        var now = _clock.UtcNow;

        var impact = ParseOptional<TicketImpact>(request.Impact) ?? TicketImpact.Medium;
        var urgency = ParseOptional<TicketUrgency>(request.Urgency) ?? TicketUrgency.Medium;
        var priority = ParseOptional<TicketPriority>(request.Priority) ?? PriorityMatrix.Resolve(impact, urgency);

        if (request.AssignedToUserId is Guid assignee
            && !await _db.Users.AnyAsync(u => u.TenantId == actor.TenantId && u.Id == assignee && u.IsActive, cancellationToken))
            throw new NotFoundException("Assignee was not found in this tenant.");

        List<Alert> alerts = [];
        if (type is TicketType.Incident or TicketType.Problem)
        {
            var alertIds = request.AlertIds ?? Array.Empty<Guid>();
            alerts = alertIds.Count == 0
                ? []
                : await _db.Alerts.Where(a => a.TenantId == actor.TenantId && alertIds.Contains(a.Id)).ToListAsync(cancellationToken);
            if (alerts.Count != alertIds.Count) throw new NotFoundException("One or more alerts were not found in this tenant.");

            if (request.RelatedBatchJobExecutionId is Guid executionId
                && !await _db.BatchJobExecutions.AnyAsync(e => e.TenantId == actor.TenantId && e.Id == executionId, cancellationToken))
                throw new NotFoundException("Related batch-job execution was not found in this tenant.");
        }

        Ticket? relatedTicket = null;
        if (request.RelatedTicketId is Guid relatedId)
        {
            relatedTicket = await _db.Tickets.SingleOrDefaultAsync(
                t => t.TenantId == actor.TenantId && t.Id == relatedId
                     && t.Status != TicketStatus.Closed && t.Status != TicketStatus.Cancelled && t.Status != TicketStatus.Rejected,
                cancellationToken)
                ?? throw new NotFoundException("An open related ticket was not found in this tenant.");
        }

        if (type == TicketType.Change)
        {
            if (string.IsNullOrWhiteSpace(actor.Subject))
                throw new ForbiddenException("A stable user subject is required to request a change.");
        }

        var ticket = new Ticket
        {
            Id = Guid.NewGuid(), TenantId = actor.TenantId, Type = type,
            Number = await IssueNumberAsync(actor.TenantId, type, cancellationToken),
            Title = request.Title.Trim(), Description = request.Description.Trim(),
            Category = request.Category?.Trim(), Status = InitialStatus(type), Priority = priority,
            Impact = impact, Urgency = urgency,
            RequestedByUserId = actor.UserId, RequestedBySubject = actor.Subject, RequestedByDisplay = actor.DisplayName,
            AssignedToUserId = request.AssignedToUserId,
            RelatedBatchJobExecutionId = type is TicketType.Incident or TicketType.Problem ? request.RelatedBatchJobExecutionId : null,
            Risk = type == TicketType.Change ? request.Risk?.Trim() : null,
            TargetEnvironment = type == TicketType.Change ? request.TargetEnvironment?.Trim() : null,
            PlannedStartAt = type == TicketType.Change ? request.PlannedStartAt?.ToUniversalTime() : null,
            PlannedEndAt = type == TicketType.Change ? request.PlannedEndAt?.ToUniversalTime() : null,
            OpenedAt = now, CreatedAt = now,
            Alerts = alerts.Select(a => new TicketAlert
            {
                TenantId = actor.TenantId, AlertId = a.Id, LinkedAt = now, LinkedByUserId = actor.UserId, CreatedAt = now
            }).ToList(),
            Activities = [new TicketActivity
            {
                TenantId = actor.TenantId, Kind = "created", ToStatus = InitialStatus(type).ToString(),
                ActorUserId = actor.UserId, ActorDisplay = actor.DisplayName, OccurredAt = now, CreatedAt = now
            }]
        };

        if (type == TicketType.Change)
        {
            var policy = await _db.Tenants.SingleOrDefaultAsync(t => t.Id == actor.TenantId, cancellationToken)
                         ?? throw new NotFoundException("Tenant not found.");
            ticket.RequiredApprovals = Math.Clamp(policy.RequiredChangeApprovals, 1, 5);
        }

        if (relatedTicket is not null)
        {
            ticket.Activities.Add(new TicketActivity
            {
                TenantId = actor.TenantId, Kind = "linked", ToStatus = relatedTicket.Number,
                Detail = $"Related to {relatedTicket.Number}", ActorUserId = actor.UserId, ActorDisplay = actor.DisplayName,
                OccurredAt = now, CreatedAt = now
            });
        }

        _db.Tickets.Add(ticket);
        await _db.SaveChangesAsync(cancellationToken);

        if (relatedTicket is not null)
        {
            _db.TicketLinks.Add(new TicketLink
            {
                TenantId = actor.TenantId, SourceTicketId = ticket.Id, TargetTicketId = relatedTicket.Id,
                LinkType = TicketLinkType.RelatesTo, LinkedAt = now, CreatedAt = now
            });
            await _db.SaveChangesAsync(cancellationToken);
        }

        await _audit.WriteAsync(actor.TenantId, AuditActorType.User, actor.UserId, actor.DisplayName,
            "ticket.created", "Ticket", ticket.Id.ToString(), "success",
            new { ticket.Number, ticket.Type, ticket.Priority, ticket.Impact, ticket.Urgency, ticket.Category, ticket.TargetEnvironment, RelatedTicketId = relatedTicket?.Id, AlertCount = alerts.Count },
            actor.IpAddress, cancellationToken);

        return await MapDetailAsync(ticket, CanViewInternal(actor), cancellationToken);
    }

    public async Task<TicketDto?> TransitionAsync(RequestActor actor, Guid ticketId, TransitionTicketRequest request, CancellationToken cancellationToken)
    {
        await _transitionTicket.ValidateAndThrowAsync(request, cancellationToken);
        _tenant.SetTenant(actor.TenantId);
        var ticket = await _db.Tickets
            .Include(t => t.Alerts).ThenInclude(a => a.Alert)
            .Include(t => t.Approvals)
            .SingleOrDefaultAsync(t => t.TenantId == actor.TenantId && t.Id == ticketId, cancellationToken);
        if (ticket is null) return null;

        if (ticket.Type == TicketType.Change)
            throw new ConflictException("Change tickets are decided through approval, not status transitions.");

        var next = Enum.Parse<TicketStatus>(request.Status, true);
        if (!CanTransition(ticket.Status, next))
            throw new ConflictException($"Ticket cannot move from {ticket.Status} to {next}.");

        var reopening = ticket.Status is TicketStatus.Resolved or TicketStatus.Closed && next == TicketStatus.InProgress;
        if (reopening && string.IsNullOrWhiteSpace(request.Reason))
            throw new ValidationException("A reopen reason is required.");

        if (request.AssignedToUserId is Guid assignee
            && !await _db.Users.AnyAsync(u => u.TenantId == actor.TenantId && u.Id == assignee && u.IsActive, cancellationToken))
            throw new NotFoundException("Assignee was not found in this tenant.");

        var previous = ticket.Status;
        var now = _clock.UtcNow;
        ticket.Status = next;
        ticket.AssignedToUserId = request.AssignedToUserId;
        if (next == TicketStatus.Acknowledged) ticket.AcknowledgedAt = now;
        if (next == TicketStatus.Resolved) ticket.ResolvedAt = now;
        if (next == TicketStatus.Closed) ticket.ClosedAt = now;
        if (reopening)
        {
            ticket.ResolvedAt = null;
            ticket.ClosedAt = null;
        }

        _db.TicketActivities.Add(new TicketActivity
        {
            TenantId = actor.TenantId, TicketId = ticket.Id, Kind = reopening ? "reopened" : "status_changed",
            FromStatus = previous.ToString(), ToStatus = next.ToString(),
            Detail = reopening ? request.Reason?.Trim() : request.AssignedToUserId is null ? null : $"Assigned to {request.AssignedToUserId}",
            ActorUserId = actor.UserId, ActorDisplay = actor.DisplayName, OccurredAt = now, CreatedAt = now
        });

        await _db.SaveChangesAsync(cancellationToken);
        await _audit.WriteAsync(actor.TenantId, AuditActorType.User, actor.UserId, actor.DisplayName,
            reopening ? "ticket.reopened" : "ticket.status_changed", "Ticket", ticket.Id.ToString(), "success",
            new { Previous = previous.ToString(), Current = next.ToString(), ticket.AssignedToUserId, Reason = request.Reason }, actor.IpAddress, cancellationToken);
        return await MapDetailAsync(ticket, CanViewInternal(actor), cancellationToken);
    }

    public async Task<TicketDto?> DecideAsync(RequestActor actor, Guid ticketId, DecideTicketRequest request, CancellationToken cancellationToken)
    {
        await _decideTicket.ValidateAndThrowAsync(request, cancellationToken);
        if (actor.Role is not (UserRole.Approver or UserRole.TenantAdmin or UserRole.PlatformAdmin))
            throw new ForbiddenException("Only an approver or tenant administrator can decide a change ticket.");
        if (string.IsNullOrWhiteSpace(actor.Subject)) throw new ForbiddenException("A stable user subject is required to approve a change.");
        _tenant.SetTenant(actor.TenantId);
        var ticket = await _db.Tickets
            .Include(t => t.Alerts).ThenInclude(a => a.Alert)
            .Include(t => t.Approvals)
            .SingleOrDefaultAsync(t => t.TenantId == actor.TenantId && t.Id == ticketId, cancellationToken);
        if (ticket is null) return null;

        if (ticket.Type != TicketType.Change)
            throw new ConflictException("Only change tickets receive approval decisions.");
        if (ticket.Status != TicketStatus.PendingApproval)
            throw new ConflictException("Only a pending change ticket can receive a decision.");

        var tenant = await _db.Tenants.AsNoTracking().SingleAsync(t => t.Id == actor.TenantId, cancellationToken);
        if (tenant.RequireChangeSeparationOfDuties && string.Equals(ticket.RequestedBySubject, actor.Subject, StringComparison.Ordinal))
            throw new ForbiddenException("The requester cannot approve or reject their own change.");
        if (ticket.Approvals.Any(a => string.Equals(a.ApproverSubject, actor.Subject, StringComparison.Ordinal)))
            throw new ConflictException("This approver has already decided this change ticket.");

        var decision = Enum.Parse<TicketApprovalDecision>(request.Decision, true);
        var now = _clock.UtcNow;
        var priorApprovals = ticket.Approvals.Count(a => a.Decision == TicketApprovalDecision.Approved);
        _db.TicketApprovals.Add(new TicketApproval
        {
            TenantId = actor.TenantId, TicketId = ticket.Id, ApproverSubject = actor.Subject,
            ApproverUserId = actor.UserId, ApproverDisplay = actor.DisplayName, Decision = decision,
            Comment = request.Comment?.Trim(), DecidedAt = now, CreatedAt = now
        });
        ticket.ConcurrencyToken = Guid.NewGuid();
        if (decision == TicketApprovalDecision.Rejected) ticket.Status = TicketStatus.Rejected;
        else if (priorApprovals + 1 >= ticket.RequiredApprovals) ticket.Status = TicketStatus.Approved;

        _db.TicketActivities.Add(new TicketActivity
        {
            TenantId = actor.TenantId, TicketId = ticket.Id, Kind = decision == TicketApprovalDecision.Approved ? "approved" : "rejected",
            FromStatus = TicketStatus.PendingApproval.ToString(), ToStatus = ticket.Status.ToString(),
            Detail = request.Comment?.Trim(), ActorUserId = actor.UserId, ActorDisplay = actor.DisplayName,
            OccurredAt = now, CreatedAt = now
        });

        try { await _db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { throw new ConflictException("The change ticket was updated concurrently. Refresh it before deciding."); }

        await _audit.WriteAsync(actor.TenantId, AuditActorType.User, actor.UserId, actor.DisplayName,
            decision == TicketApprovalDecision.Approved ? "ticket.approved" : "ticket.rejected",
            "Ticket", ticket.Id.ToString(), "success",
            new { ticket.Number, decision, request.Comment, ticket.Status, ticket.RequiredApprovals }, actor.IpAddress, cancellationToken);
        await NotifyLinkedDeploymentsAsync(actor.TenantId, ticket.Id, cancellationToken);
        return await MapDetailAsync(ticket, CanViewInternal(actor), cancellationToken);
    }

    public async Task<TicketDto?> CancelAsync(RequestActor actor, Guid ticketId, CancellationToken cancellationToken)
    {
        _tenant.SetTenant(actor.TenantId);
        var ticket = await _db.Tickets
            .Include(t => t.Alerts).ThenInclude(a => a.Alert)
            .Include(t => t.Approvals)
            .SingleOrDefaultAsync(t => t.TenantId == actor.TenantId && t.Id == ticketId, cancellationToken);
        if (ticket is null) return null;

        if (ticket.Type != TicketType.Change)
            throw new ConflictException("Only change tickets can be cancelled.");
        if (ticket.Status != TicketStatus.PendingApproval)
            throw new ConflictException("Only a pending change ticket can be cancelled.");

        var isAdmin = actor.Role is UserRole.TenantAdmin or UserRole.PlatformAdmin;
        if (!isAdmin && !string.Equals(ticket.RequestedBySubject, actor.Subject, StringComparison.Ordinal))
            throw new ForbiddenException("Only the requester or a tenant administrator can cancel this change ticket.");

        var now = _clock.UtcNow;
        ticket.Status = TicketStatus.Cancelled;
        ticket.ConcurrencyToken = Guid.NewGuid();
        _db.TicketActivities.Add(new TicketActivity
        {
            TenantId = actor.TenantId, TicketId = ticket.Id, Kind = "cancelled", FromStatus = TicketStatus.PendingApproval.ToString(),
            ToStatus = TicketStatus.Cancelled.ToString(), ActorUserId = actor.UserId, ActorDisplay = actor.DisplayName,
            OccurredAt = now, CreatedAt = now
        });
        try { await _db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { throw new ConflictException("The change ticket was updated concurrently. Refresh it before cancelling."); }

        await _audit.WriteAsync(actor.TenantId, AuditActorType.User, actor.UserId, actor.DisplayName,
            "ticket.cancelled", "Ticket", ticket.Id.ToString(), "success",
            new { ticket.Number }, actor.IpAddress, cancellationToken);
        await NotifyLinkedDeploymentsAsync(actor.TenantId, ticket.Id, cancellationToken);
        return await MapDetailAsync(ticket, CanViewInternal(actor), cancellationToken);
    }

    public async Task<TicketDto?> AddCommentAsync(RequestActor actor, Guid ticketId, AddTicketCommentRequest request, CancellationToken cancellationToken)
    {
        await _addComment.ValidateAndThrowAsync(request, cancellationToken);
        if (request.IsInternal && actor.Role is UserRole.Viewer)
            throw new ForbiddenException("Viewer accounts cannot add internal notes.");
        _tenant.SetTenant(actor.TenantId);
        var ticket = await LoadTicketAsync(actor.TenantId, ticketId, cancellationToken);
        if (ticket is null) return null;

        var now = _clock.UtcNow;
        _db.TicketComments.Add(new TicketComment
        {
            TenantId = actor.TenantId, TicketId = ticket.Id,
            AuthorUserId = actor.UserId, AuthorSubject = NullIfEmpty(actor.Subject),
            AuthorDisplay = actor.DisplayName, Body = request.Body.Trim(), IsInternal = request.IsInternal,
            CreatedAt = now, UpdatedAt = now
        });
        _db.TicketActivities.Add(new TicketActivity
        {
            TenantId = actor.TenantId, TicketId = ticket.Id, Kind = "comment_added",
            Detail = request.IsInternal ? "Internal note added." : "Comment added.",
            ActorUserId = actor.UserId, ActorDisplay = actor.DisplayName, OccurredAt = now, CreatedAt = now
        });
        await _db.SaveChangesAsync(cancellationToken);

        await _audit.WriteAsync(actor.TenantId, AuditActorType.User, actor.UserId, actor.DisplayName,
            "ticket.comment_added", "Ticket", ticket.Id.ToString(), "success",
            new { ticket.Number, request.IsInternal }, actor.IpAddress, cancellationToken);
        return await MapDetailAsync(ticket, CanViewInternal(actor), cancellationToken);
    }

    public async Task<TicketDto?> AddWorklogAsync(RequestActor actor, Guid ticketId, AddTicketWorklogRequest request, CancellationToken cancellationToken)
    {
        await _addWorklog.ValidateAndThrowAsync(request, cancellationToken);
        _tenant.SetTenant(actor.TenantId);
        var ticket = await LoadTicketAsync(actor.TenantId, ticketId, cancellationToken);
        if (ticket is null) return null;

        var now = _clock.UtcNow;
        _db.TicketWorklogs.Add(new TicketWorklog
        {
            TenantId = actor.TenantId, TicketId = ticket.Id,
            AuthorUserId = actor.UserId, AuthorSubject = NullIfEmpty(actor.Subject),
            AuthorDisplay = actor.DisplayName, Minutes = request.Minutes, Note = request.Note?.Trim(),
            WorkedAt = now, CreatedAt = now, UpdatedAt = now
        });
        _db.TicketActivities.Add(new TicketActivity
        {
            TenantId = actor.TenantId, TicketId = ticket.Id, Kind = "worklog_added",
            Detail = $"{request.Minutes} minutes logged.",
            ActorUserId = actor.UserId, ActorDisplay = actor.DisplayName, OccurredAt = now, CreatedAt = now
        });
        await _db.SaveChangesAsync(cancellationToken);

        await _audit.WriteAsync(actor.TenantId, AuditActorType.User, actor.UserId, actor.DisplayName,
            "ticket.worklog_added", "Ticket", ticket.Id.ToString(), "success",
            new { ticket.Number, request.Minutes }, actor.IpAddress, cancellationToken);
        return await MapDetailAsync(ticket, CanViewInternal(actor), cancellationToken);
    }

    public async Task<TicketDto?> SetWatchingAsync(RequestActor actor, Guid ticketId, bool watch, CancellationToken cancellationToken)
    {
        _tenant.SetTenant(actor.TenantId);
        var ticket = await LoadTicketAsync(actor.TenantId, ticketId, cancellationToken);
        if (ticket is null) return null;

        var subject = WatcherSubject(actor);
        var existing = await _db.TicketWatchers.SingleOrDefaultAsync(
            w => w.TenantId == actor.TenantId && w.TicketId == ticket.Id && w.Subject == subject, cancellationToken);

        if (watch && existing is null)
        {
            var now = _clock.UtcNow;
            _db.TicketWatchers.Add(new TicketWatcher
            {
                TenantId = actor.TenantId, TicketId = ticket.Id, UserId = actor.UserId,
                Subject = subject, Display = actor.DisplayName, CreatedAt = now, UpdatedAt = now
            });
            await _db.SaveChangesAsync(cancellationToken);
        }
        else if (!watch && existing is not null)
        {
            _db.TicketWatchers.Remove(existing);
            await _db.SaveChangesAsync(cancellationToken);
        }

        return await MapDetailAsync(ticket, CanViewInternal(actor), cancellationToken);
    }

    public async Task<TicketDto?> SetDueDateAsync(RequestActor actor, Guid ticketId, DateTimeOffset? dueAt, CancellationToken cancellationToken)
    {
        await _setDueDate.ValidateAndThrowAsync(new SetTicketDueDateRequest(dueAt), cancellationToken);
        _tenant.SetTenant(actor.TenantId);
        var ticket = await LoadTicketAsync(actor.TenantId, ticketId, cancellationToken);
        if (ticket is null) return null;

        var previous = ticket.DueAt;
        var now = _clock.UtcNow;
        ticket.DueAt = dueAt;
        ticket.UpdatedAt = now;
        _db.TicketActivities.Add(new TicketActivity
        {
            TenantId = actor.TenantId, TicketId = ticket.Id, Kind = "due_date_changed",
            Detail = dueAt is null ? "Due date cleared." : $"Due date set to {dueAt:O}.",
            ActorUserId = actor.UserId, ActorDisplay = actor.DisplayName, OccurredAt = now, CreatedAt = now
        });
        await _db.SaveChangesAsync(cancellationToken);

        await _audit.WriteAsync(actor.TenantId, AuditActorType.User, actor.UserId, actor.DisplayName,
            "ticket.due_date_changed", "Ticket", ticket.Id.ToString(), "success",
            new { ticket.Number, Previous = previous, ticket.DueAt }, actor.IpAddress, cancellationToken);
        return await MapDetailAsync(ticket, CanViewInternal(actor), cancellationToken);
    }

    public async Task<TicketDto?> AddAttachmentAsync(RequestActor actor, Guid ticketId, UploadTicketAttachmentRequest request, CancellationToken cancellationToken)
    {
        if (request.Content is null || request.Content.Length is <= 0 or > TicketAttachmentLimits.MaximumBytes)
            throw new ValidationException($"Attachments must be between 1 byte and {TicketAttachmentLimits.MaximumBytes / (1024 * 1024)} MiB.");
        _tenant.SetTenant(actor.TenantId);
        var ticket = await LoadTicketAsync(actor.TenantId, ticketId, cancellationToken);
        if (ticket is null) return null;

        var count = await _db.TicketAttachments.CountAsync(
            a => a.TenantId == actor.TenantId && a.TicketId == ticket.Id, cancellationToken);
        if (count >= TicketAttachmentLimits.MaximumPerTicket)
            throw new ConflictException($"A ticket can hold at most {TicketAttachmentLimits.MaximumPerTicket} attachments.");

        var now = _clock.UtcNow;
        var fileName = SanitizeFileName(request.FileName);
        var contentType = string.IsNullOrWhiteSpace(request.ContentType) ? "application/octet-stream" : request.ContentType.Trim();
        if (contentType.Length > 100) contentType = contentType[..100];

        var attachment = new TicketAttachment
        {
            TenantId = actor.TenantId, TicketId = ticket.Id,
            FileName = fileName, ContentType = contentType, SizeBytes = request.Content.LongLength,
            Sha256 = Convert.ToHexString(SHA256.HashData(request.Content)).ToLowerInvariant(),
            Content = request.Content,
            UploadedByUserId = actor.UserId, UploadedByDisplay = actor.DisplayName,
            UploadedAt = now, CreatedAt = now, UpdatedAt = now
        };
        _db.TicketAttachments.Add(attachment);
        _db.TicketActivities.Add(new TicketActivity
        {
            TenantId = actor.TenantId, TicketId = ticket.Id, Kind = "attachment_added",
            Detail = fileName, ActorUserId = actor.UserId, ActorDisplay = actor.DisplayName,
            OccurredAt = now, CreatedAt = now
        });
        await _db.SaveChangesAsync(cancellationToken);

        await _audit.WriteAsync(actor.TenantId, AuditActorType.User, actor.UserId, actor.DisplayName,
            "ticket.attachment_added", "Ticket", ticket.Id.ToString(), "success",
            new { ticket.Number, attachment.Id, attachment.FileName, attachment.SizeBytes, attachment.Sha256 },
            actor.IpAddress, cancellationToken);
        return await MapDetailAsync(ticket, CanViewInternal(actor), cancellationToken);
    }

    public async Task<TicketDto?> DeleteAttachmentAsync(RequestActor actor, Guid ticketId, Guid attachmentId, CancellationToken cancellationToken)
    {
        _tenant.SetTenant(actor.TenantId);
        var ticket = await LoadTicketAsync(actor.TenantId, ticketId, cancellationToken);
        if (ticket is null) return null;

        var attachment = await _db.TicketAttachments.SingleOrDefaultAsync(
            a => a.TenantId == actor.TenantId && a.TicketId == ticket.Id && a.Id == attachmentId, cancellationToken)
            ?? throw new NotFoundException("Attachment not found in this ticket.");

        _db.TicketAttachments.Remove(attachment);
        var now = _clock.UtcNow;
        _db.TicketActivities.Add(new TicketActivity
        {
            TenantId = actor.TenantId, TicketId = ticket.Id, Kind = "attachment_removed",
            Detail = attachment.FileName, ActorUserId = actor.UserId, ActorDisplay = actor.DisplayName,
            OccurredAt = now, CreatedAt = now
        });
        await _db.SaveChangesAsync(cancellationToken);

        await _audit.WriteAsync(actor.TenantId, AuditActorType.User, actor.UserId, actor.DisplayName,
            "ticket.attachment_removed", "Ticket", ticket.Id.ToString(), "success",
            new { ticket.Number, attachment.Id, attachment.FileName }, actor.IpAddress, cancellationToken);
        return await MapDetailAsync(ticket, CanViewInternal(actor), cancellationToken);
    }

    public async Task<TicketAttachmentContent?> GetAttachmentAsync(Guid tenantId, Guid ticketId, Guid attachmentId, CancellationToken cancellationToken)
    {
        _tenant.SetTenant(tenantId);
        return await _db.TicketAttachments.AsNoTracking()
            .Where(a => a.TenantId == tenantId && a.TicketId == ticketId && a.Id == attachmentId)
            .Select(a => new TicketAttachmentContent(a.FileName, a.ContentType, a.Content))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<ChangeApprovalPolicyDto> GetPolicyAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        _tenant.SetTenant(tenantId);
        var tenant = await _db.Tenants.AsNoTracking().SingleOrDefaultAsync(t => t.Id == tenantId, cancellationToken)
                     ?? throw new NotFoundException("Tenant not found.");
        return new ChangeApprovalPolicyDto(tenant.RequiredChangeApprovals, tenant.RequireChangeSeparationOfDuties);
    }

    public async Task<ChangeApprovalPolicyDto> UpdatePolicyAsync(RequestActor actor, UpdateChangeApprovalPolicyRequest request, CancellationToken cancellationToken)
    {
        await _policyValidator.ValidateAndThrowAsync(request, cancellationToken);
        _tenant.SetTenant(actor.TenantId);
        var tenant = await _db.Tenants.SingleOrDefaultAsync(t => t.Id == actor.TenantId, cancellationToken)
                     ?? throw new NotFoundException("Tenant not found.");
        tenant.RequiredChangeApprovals = request.RequiredApprovals;
        tenant.RequireChangeSeparationOfDuties = request.RequireSeparationOfDuties;
        await _db.SaveChangesAsync(cancellationToken);
        await _audit.WriteAsync(actor.TenantId, AuditActorType.User, actor.UserId, actor.DisplayName,
            "change_policy.updated", "Tenant", actor.TenantId.ToString(), "success", request, actor.IpAddress, cancellationToken);
        return new ChangeApprovalPolicyDto(tenant.RequiredChangeApprovals, tenant.RequireChangeSeparationOfDuties);
    }

    private async Task<string> IssueNumberAsync(Guid tenantId, TicketType type, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var sequence = await _db.TicketNumberSequences
                .SingleOrDefaultAsync(s => s.TenantId == tenantId && s.Type == type, cancellationToken);
            if (sequence is null)
            {
                sequence = new TicketNumberSequence
                {
                    Id = Guid.NewGuid(), TenantId = tenantId, Type = type, LastIssued = 0,
                    CreatedAt = _clock.UtcNow, UpdatedAt = _clock.UtcNow
                };
                _db.TicketNumberSequences.Add(sequence);
            }
            sequence.LastIssued++;
            sequence.ConcurrencyToken = Guid.NewGuid();
            try
            {
                await _db.SaveChangesAsync(cancellationToken);
                return $"{PrefixFor(type)}-{sequence.LastIssued:D6}";
            }
            catch (DbUpdateException)
            {
                _db.ClearTracking();
            }
        }
        throw new ConflictException("Could not allocate a ticket number. Retry the request.");
    }

    private async Task NotifyLinkedDeploymentsAsync(Guid tenantId, Guid ticketId, CancellationToken cancellationToken)
    {
        var status = await _db.Tickets.AsNoTracking()
            .Where(t => t.TenantId == tenantId && t.Id == ticketId)
            .Select(t => t.Status)
            .SingleAsync(cancellationToken);

        var next = status switch
        {
            TicketStatus.Approved => DeploymentRequestStatus.Approved,
            TicketStatus.Rejected => DeploymentRequestStatus.Rejected,
            TicketStatus.Cancelled => DeploymentRequestStatus.Cancelled,
            _ => (DeploymentRequestStatus?)null
        };

        if (next is not null)
        {
            await _db.DeploymentRequests
                .Where(deployment => deployment.TenantId == tenantId
                                     && deployment.ChangeTicketId == ticketId
                                     && deployment.Status == DeploymentRequestStatus.PendingApproval)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(deployment => deployment.Status, next.Value)
                    .SetProperty(deployment => deployment.UpdatedAt, _clock.UtcNow), cancellationToken);
        }

        var deploymentIds = await _db.DeploymentRequests.AsNoTracking()
            .Where(deployment => deployment.TenantId == tenantId && deployment.ChangeTicketId == ticketId)
            .Select(deployment => deployment.Id)
            .ToListAsync(cancellationToken);
        foreach (var deploymentId in deploymentIds)
            await _realtime.DeploymentChangedAsync(tenantId, deploymentId, cancellationToken);
    }

    private async Task<TicketDto> MapDetailAsync(Ticket ticket, bool includeInternal, CancellationToken cancellationToken)
    {
        var links = await _db.TicketLinks.AsNoTracking()
            .Where(l => l.TenantId == ticket.TenantId && (l.SourceTicketId == ticket.Id || l.TargetTicketId == ticket.Id))
            .ToListAsync(cancellationToken);
        var otherIds = links.Select(l => l.SourceTicketId == ticket.Id ? l.TargetTicketId : l.SourceTicketId).Distinct().ToArray();
        var others = otherIds.Length == 0
            ? []
            : await _db.Tickets.AsNoTracking()
                .Where(t => t.TenantId == ticket.TenantId && otherIds.Contains(t.Id))
                .Select(t => new { t.Id, t.Number, t.Type })
                .ToDictionaryAsync(t => t.Id, cancellationToken);
        var related = links
            .Where(l => others.ContainsKey(l.SourceTicketId == ticket.Id ? l.TargetTicketId : l.SourceTicketId))
            .Select(l =>
            {
                var otherId = l.SourceTicketId == ticket.Id ? l.TargetTicketId : l.SourceTicketId;
                var other = others[otherId];
                return new TicketLinkDto(other.Id, other.Number, other.Type.ToString(), l.LinkType.ToString());
            }).ToArray();

        var comments = await _db.TicketComments.AsNoTracking()
            .Where(c => c.TenantId == ticket.TenantId && c.TicketId == ticket.Id && (includeInternal || !c.IsInternal))
            .OrderBy(c => c.CreatedAt)
            .ToListAsync(cancellationToken);
        var worklogs = await _db.TicketWorklogs.AsNoTracking()
            .Where(w => w.TenantId == ticket.TenantId && w.TicketId == ticket.Id)
            .OrderBy(w => w.WorkedAt)
            .ToListAsync(cancellationToken);
        var watchers = await _db.TicketWatchers.AsNoTracking()
            .Where(w => w.TenantId == ticket.TenantId && w.TicketId == ticket.Id)
            .OrderBy(w => w.CreatedAt)
            .ToListAsync(cancellationToken);
        var attachments = await _db.TicketAttachments.AsNoTracking()
            .Where(a => a.TenantId == ticket.TenantId && a.TicketId == ticket.Id)
            .OrderBy(a => a.UploadedAt)
            .Select(a => new TicketAttachmentDto(a.Id, a.FileName, a.ContentType, a.SizeBytes, a.Sha256, a.UploadedByDisplay, a.UploadedAt))
            .ToListAsync(cancellationToken);

        return Map(
            ticket,
            related,
            comments.Select(c => new TicketCommentDto(c.Id, c.Body, c.IsInternal, c.AuthorDisplay, c.CreatedAt)).ToArray(),
            worklogs.Select(w => new TicketWorklogDto(w.Id, w.Minutes, w.Note, w.AuthorDisplay, w.WorkedAt)).ToArray(),
            watchers.Select(w => new TicketWatcherDto(w.Id, w.UserId, w.Display, w.CreatedAt)).ToArray(),
            attachments);
    }

    private static TicketDto MapSummary(Ticket ticket) => Map(ticket, [], [], [], [], []);

    private Task<Ticket?> LoadTicketAsync(Guid tenantId, Guid ticketId, CancellationToken cancellationToken) =>
        _db.Tickets
            .Include(t => t.Alerts).ThenInclude(a => a.Alert)
            .Include(t => t.Approvals)
            .Include(t => t.Activities)
            .SingleOrDefaultAsync(t => t.TenantId == tenantId && t.Id == ticketId, cancellationToken);

    private static bool CanViewInternal(RequestActor actor) =>
        actor.Role is UserRole.Operator or UserRole.Approver or UserRole.TenantAdmin or UserRole.PlatformAdmin;

    private static string WatcherSubject(RequestActor actor) =>
        !string.IsNullOrWhiteSpace(actor.Subject) ? actor.Subject
        : actor.UserId is Guid userId ? userId.ToString()
        : actor.DisplayName;

    private static string? NullIfEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;

    private static string SanitizeFileName(string? value)
    {
        var name = Path.GetFileName(value?.Trim() ?? string.Empty);
        if (string.IsNullOrWhiteSpace(name)) name = "attachment";
        return name.Length <= 255 ? name : name[^255..];
    }

    private static TicketDto Map(
        Ticket ticket,
        IReadOnlyList<TicketLinkDto> related,
        IReadOnlyList<TicketCommentDto> comments,
        IReadOnlyList<TicketWorklogDto> worklogs,
        IReadOnlyList<TicketWatcherDto> watchers,
        IReadOnlyList<TicketAttachmentDto> attachments) => new(
        ticket.Id, ticket.Number, ticket.Type.ToString(), ticket.Title, ticket.Description, ticket.Category,
        ticket.Status.ToString(), ticket.Priority.ToString(), ticket.Impact.ToString(), ticket.Urgency.ToString(),
        ticket.RequestedByUserId, ticket.RequestedByDisplay, ticket.AssignedToUserId, ticket.RelatedBatchJobExecutionId,
        ticket.Risk, ticket.TargetEnvironment, ticket.PlannedStartAt, ticket.PlannedEndAt, ticket.RequiredApprovals,
        ticket.OpenedAt, ticket.AcknowledgedAt, ticket.ResolvedAt, ticket.ClosedAt,
        ticket.Alerts.Select(link => new TicketAlertDto(link.AlertId, link.Alert.Severity.ToString(), link.Alert.Status.ToString(), link.Alert.Message, link.Alert.TriggeredAt)).ToArray(),
        ticket.Approvals.OrderBy(a => a.DecidedAt).Select(a => new TicketApprovalDto(a.ApproverDisplay, a.Decision.ToString(), a.Comment, a.DecidedAt)).ToArray(),
        ticket.Activities.OrderBy(a => a.OccurredAt).Select(a => new TicketActivityDto(a.Kind, a.FromStatus, a.ToStatus, a.Detail, a.ActorDisplay, a.OccurredAt)).ToArray(),
        related, comments, worklogs, watchers, attachments, ticket.DueAt);

    private static TicketStatus InitialStatus(TicketType type) =>
        type == TicketType.Change ? TicketStatus.PendingApproval : TicketStatus.New;

    private static bool CanTransition(TicketStatus from, TicketStatus to) => (from, to) switch
    {
        (TicketStatus.New, TicketStatus.Acknowledged or TicketStatus.InProgress or TicketStatus.Resolved) => true,
        (TicketStatus.Acknowledged, TicketStatus.InProgress or TicketStatus.Resolved) => true,
        (TicketStatus.InProgress, TicketStatus.Resolved) => true,
        (TicketStatus.Resolved, TicketStatus.Closed or TicketStatus.InProgress) => true,
        (TicketStatus.Closed, TicketStatus.InProgress) => true,
        _ => false
    };

    private static string PrefixFor(TicketType type) => type switch
    {
        TicketType.Incident => "INC",
        TicketType.ServiceRequest => "SRV",
        TicketType.Problem => "PRB",
        TicketType.Change => "CHG",
        _ => throw new ArgumentOutOfRangeException(nameof(type))
    };

    private static T ParseEnum<T>(string value, string field) where T : struct, Enum =>
        Enum.TryParse<T>(value, true, out var parsed) && Enum.IsDefined(parsed)
            ? parsed
            : throw new ValidationException($"Unknown {field}.");

    private static T? ParseOptional<T>(string? value) where T : struct, Enum =>
        string.IsNullOrWhiteSpace(value) ? null : Enum.Parse<T>(value, true);
}

public static class PriorityMatrix
{
    public static TicketPriority Resolve(TicketImpact impact, TicketUrgency urgency) => (impact, urgency) switch
    {
        (TicketImpact.High, TicketUrgency.High) => TicketPriority.Critical,
        (TicketImpact.High, TicketUrgency.Medium) => TicketPriority.High,
        (TicketImpact.High, TicketUrgency.Low) => TicketPriority.Medium,
        (TicketImpact.Medium, TicketUrgency.High) => TicketPriority.High,
        (TicketImpact.Medium, TicketUrgency.Medium) => TicketPriority.Medium,
        (TicketImpact.Medium, TicketUrgency.Low) => TicketPriority.Low,
        (TicketImpact.Low, TicketUrgency.High) => TicketPriority.Medium,
        (TicketImpact.Low, TicketUrgency.Medium) => TicketPriority.Low,
        (TicketImpact.Low, TicketUrgency.Low) => TicketPriority.Low,
        _ => TicketPriority.Medium
    };
}
