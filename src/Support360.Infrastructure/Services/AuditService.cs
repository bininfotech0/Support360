using System.Text.Json;
using Support360.Application.Abstractions;
using Support360.Domain;
using Support360.Domain.Entities;

namespace Support360.Infrastructure.Services;

public sealed class AuditService : IAuditService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ISupport360DbContext _db;
    private readonly IClock _clock;

    public AuditService(ISupport360DbContext db, IClock clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task WriteAsync(
        Guid tenantId,
        AuditActorType actorType,
        Guid? actorId,
        string? actorDisplay,
        string action,
        string targetType,
        string? targetId,
        string outcome,
        object? details,
        string? ipAddress,
        CancellationToken cancellationToken)
    {
        var json = details is null ? null : JsonSerializer.Serialize(details, JsonOptions);
        if (json is { Length: > 8000 })
        {
            json = json[..8000];
        }

        _db.AuditEvents.Add(new AuditEvent
        {
            TenantId = tenantId,
            ActorType = actorType,
            ActorId = actorId,
            ActorDisplay = actorDisplay,
            Action = action,
            TargetType = targetType,
            TargetId = targetId,
            Outcome = outcome,
            DetailsJson = json,
            IpAddress = ipAddress,
            CreatedAt = _clock.UtcNow
        });

        await _db.SaveChangesAsync(cancellationToken);
    }
}
