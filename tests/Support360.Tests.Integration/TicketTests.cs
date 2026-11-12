using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Support360.Application.Abstractions;
using Support360.Contracts.Auth;
using Support360.Contracts.Operations;
using Support360.Domain;
using Support360.Domain.Entities;
using Support360.Infrastructure.Persistence;

namespace Support360.Tests.Integration;

public sealed class TicketTests : IClassFixture<Support360ApiFactory>
{
    private const string Password = "Ticket-Test-Password-123!";

    private readonly Support360ApiFactory _factory;

    public TicketTests(Support360ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task WorkTicketLifecycleEnforcesTransitionsNumberingAndPriorityMatrix()
    {
        using var admin = await CreateAuthorizedClientAsync(Support360ApiFactory.AdminEmail, Support360ApiFactory.AdminPassword);

        var invalidCreate = await admin.PostAsJsonAsync("/api/tickets", new CreateTicketRequest("Incident", "", " ", null, null, null, null, null, null, null, null, null, null, null, null));
        Assert.Equal(HttpStatusCode.BadRequest, invalidCreate.StatusCode);

        var create = await admin.PostAsJsonAsync("/api/tickets", new CreateTicketRequest(
            "Incident", "Database latency", "Database response time increased for the primary tenant.", null,
            null, "High", "High", null, [], null, null, null, null, null, null));
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var incident = (await create.Content.ReadFromJsonAsync<TicketDto>())!;
        Assert.StartsWith("INC-", incident.Number);
        Assert.Equal("New", incident.Status);
        Assert.Equal("Critical", incident.Priority);
        Assert.Equal("High", incident.Impact);
        Assert.Equal("High", incident.Urgency);
        Assert.Equal("Integration Admin", incident.RequestedByDisplay);
        Assert.Single(incident.Activities, a => a.Kind == "created");

        var invalidTransition = await admin.PutAsJsonAsync($"/api/tickets/{incident.Id}/status", new TransitionTicketRequest("Closed", null, null));
        Assert.Equal(HttpStatusCode.Conflict, invalidTransition.StatusCode);

        var acknowledge = await admin.PutAsJsonAsync($"/api/tickets/{incident.Id}/status", new TransitionTicketRequest("Acknowledged", null, null));
        acknowledge.EnsureSuccessStatusCode();
        var acknowledged = (await acknowledge.Content.ReadFromJsonAsync<TicketDto>())!;
        Assert.Equal("Acknowledged", acknowledged.Status);
        Assert.NotNull(acknowledged.AcknowledgedAt);

        var progress = await admin.PutAsJsonAsync($"/api/tickets/{incident.Id}/status", new TransitionTicketRequest("InProgress", null, null));
        progress.EnsureSuccessStatusCode();
        var resolve = await admin.PutAsJsonAsync($"/api/tickets/{incident.Id}/status", new TransitionTicketRequest("Resolved", null, null));
        resolve.EnsureSuccessStatusCode();
        var resolved = (await resolve.Content.ReadFromJsonAsync<TicketDto>())!;
        Assert.Equal("Resolved", resolved.Status);
        Assert.NotNull(resolved.ResolvedAt);

        var reopenWithoutReason = await admin.PutAsJsonAsync($"/api/tickets/{incident.Id}/status", new TransitionTicketRequest("InProgress", null, null));
        Assert.Equal(HttpStatusCode.BadRequest, reopenWithoutReason.StatusCode);
        var reopen = await admin.PutAsJsonAsync($"/api/tickets/{incident.Id}/status", new TransitionTicketRequest("InProgress", null, "Customer reported the issue again."));
        reopen.EnsureSuccessStatusCode();
        var reopened = (await reopen.Content.ReadFromJsonAsync<TicketDto>())!;
        Assert.Equal("InProgress", reopened.Status);
        Assert.Null(reopened.ResolvedAt);
        Assert.Contains(reopened.Activities, a => a.Kind == "reopened");

        var close = await admin.PutAsJsonAsync($"/api/tickets/{incident.Id}/status", new TransitionTicketRequest("Resolved", null, null));
        close.EnsureSuccessStatusCode();
        var finalClose = await admin.PutAsJsonAsync($"/api/tickets/{incident.Id}/status", new TransitionTicketRequest("Closed", null, null));
        finalClose.EnsureSuccessStatusCode();
        Assert.Equal("Closed", (await finalClose.Content.ReadFromJsonAsync<TicketDto>())!.Status);

        var second = await admin.PostAsJsonAsync("/api/tickets", new CreateTicketRequest(
            "Incident", "Second incident", "Verifies sequence numbering.", null, null, "Low", "Low", null, null, null, null, null, null, null, null));
        second.EnsureSuccessStatusCode();
        var secondIncident = (await second.Content.ReadFromJsonAsync<TicketDto>())!;
        Assert.Equal("Low", secondIncident.Priority);
        var firstIndex = int.Parse(incident.Number["INC-".Length..]);
        Assert.Equal(firstIndex + 1, int.Parse(secondIncident.Number["INC-".Length..]));

        var serviceRequest = await admin.PostAsJsonAsync("/api/tickets", new CreateTicketRequest(
            "ServiceRequest", "VPN access", "Grant VPN access for the on-call engineer.", "Access", null, null, null, null, null, null, null, null, null, null, null));
        Assert.Equal(HttpStatusCode.Created, serviceRequest.StatusCode);
        var request = (await serviceRequest.Content.ReadFromJsonAsync<TicketDto>())!;
        Assert.StartsWith("SRV-", request.Number);
        Assert.Equal("New", request.Status);
        Assert.Equal("Access", request.Category);

        var missingCategory = await admin.PostAsJsonAsync("/api/tickets", new CreateTicketRequest(
            "ServiceRequest", "No category", "Category is required for service requests.", null, null, null, null, null, null, null, null, null, null, null, null));
        Assert.Equal(HttpStatusCode.BadRequest, missingCategory.StatusCode);

        var explicitPriority = await admin.PostAsJsonAsync("/api/tickets", new CreateTicketRequest(
            "ServiceRequest", "Explicit priority", "An explicit priority overrides the matrix.", "Hardware", "Critical", "Low", "Low", null, null, null, null, null, null, null, null));
        explicitPriority.EnsureSuccessStatusCode();
        Assert.Equal("Critical", (await explicitPriority.Content.ReadFromJsonAsync<TicketDto>())!.Priority);

        var resolvedOnly = await admin.GetFromJsonAsync<List<TicketDto>>("/api/tickets?status=resolved");
        Assert.DoesNotContain(resolvedOnly!, item => item.Id == incident.Id);
        var incidentsOnly = await admin.GetFromJsonAsync<List<TicketDto>>("/api/tickets?type=incident");
        Assert.DoesNotContain(incidentsOnly!, item => item.Number.StartsWith("SRV-"));
        var unknownStatus = await admin.GetAsync("/api/tickets?status=nonsense");
        Assert.Equal(HttpStatusCode.BadRequest, unknownStatus.StatusCode);
        var search = await admin.GetFromJsonAsync<List<TicketDto>>($"/api/tickets?q={incident.Number.ToLowerInvariant()}");
        Assert.Contains(search!, item => item.Id == incident.Id);
    }

    [Fact]
    public async Task ChangeTicketsEnforcePolicySeparationOfDutiesAndApprovalThreshold()
    {
        using var admin = await CreateAuthorizedClientAsync(Support360ApiFactory.AdminEmail, Support360ApiFactory.AdminPassword);

        var policy = await admin.PutAsJsonAsync("/api/change-management/policy", new UpdateChangeApprovalPolicyRequest(2, true));
        policy.EnsureSuccessStatusCode();

        var incident = await CreateWorkTicketAsync(admin, "Incident", "Root cause", "Underlying incident for the change.");

        var invalidChange = await admin.PostAsJsonAsync("/api/tickets", new CreateTicketRequest(
            "Change", "No risk", "Change without a risk assessment.", null, null, null, null, null, null, null, null, null,
            DateTimeOffset.UtcNow.AddHours(2), DateTimeOffset.UtcNow.AddHours(3), null));
        Assert.Equal(HttpStatusCode.BadRequest, invalidChange.StatusCode);

        var pastWindow = await admin.PostAsJsonAsync("/api/tickets", new CreateTicketRequest(
            "Change", "Past window", "Window must be in the future.", null, null, null, null, null, null, null, "low", "production",
            DateTimeOffset.UtcNow.AddHours(-2), DateTimeOffset.UtcNow.AddHours(-1), null));
        Assert.Equal(HttpStatusCode.BadRequest, pastWindow.StatusCode);

        var create = await admin.PostAsJsonAsync("/api/tickets", new CreateTicketRequest(
            "Change", "Database index maintenance", "Apply the approved index maintenance plan.", null, null, null, null,
            null, null, null, "Medium risk; monitor write latency.", "production",
            DateTimeOffset.UtcNow.AddHours(2), DateTimeOffset.UtcNow.AddHours(3), incident.Id));
        create.EnsureSuccessStatusCode();
        var change = (await create.Content.ReadFromJsonAsync<TicketDto>())!;
        Assert.StartsWith("CHG-", change.Number);
        Assert.Equal("PendingApproval", change.Status);
        Assert.Equal(2, change.RequiredApprovals);

        var detail = await admin.GetFromJsonAsync<TicketDto>($"/api/tickets/{change.Id}");
        Assert.Contains(detail!.RelatedTickets, link => link.TicketId == incident.Id && link.LinkType == "RelatesTo");
        var incidentDetail = await admin.GetFromJsonAsync<TicketDto>($"/api/tickets/{incident.Id}");
        Assert.Contains(incidentDetail!.RelatedTickets, link => link.TicketId == change.Id);

        var transitionAttempt = await admin.PutAsJsonAsync($"/api/tickets/{change.Id}/status", new TransitionTicketRequest("Approved", null, null));
        Assert.Equal(HttpStatusCode.Conflict, transitionAttempt.StatusCode);

        var selfApproval = await admin.PostAsJsonAsync($"/api/tickets/{change.Id}/decision", new DecideTicketRequest("Approved", "I requested this."));
        Assert.Equal(HttpStatusCode.Forbidden, selfApproval.StatusCode);

        var approvers = await CreateApproversAsync();
        using var firstApprover = await CreateAuthorizedClientAsync(approvers[0].Email, approvers[0].Password);
        using var secondApprover = await CreateAuthorizedClientAsync(approvers[1].Email, approvers[1].Password);

        var firstDecision = await firstApprover.PostAsJsonAsync($"/api/tickets/{change.Id}/decision", new DecideTicketRequest("Approved", "Reviewed risk."));
        firstDecision.EnsureSuccessStatusCode();
        Assert.Equal("PendingApproval", (await firstDecision.Content.ReadFromJsonAsync<TicketDto>())!.Status);

        var duplicateDecision = await firstApprover.PostAsJsonAsync($"/api/tickets/{change.Id}/decision", new DecideTicketRequest("Approved", null));
        Assert.Equal(HttpStatusCode.Conflict, duplicateDecision.StatusCode);

        var secondDecision = await secondApprover.PostAsJsonAsync($"/api/tickets/{change.Id}/decision", new DecideTicketRequest("Approved", "Approved."));
        secondDecision.EnsureSuccessStatusCode();
        var approved = (await secondDecision.Content.ReadFromJsonAsync<TicketDto>())!;
        Assert.Equal("Approved", approved.Status);
        Assert.Equal(2, approved.Approvals.Count);
        Assert.Contains(approved.Activities, a => a.Kind == "approved");

        var cancellable = await admin.PostAsJsonAsync("/api/tickets", new CreateTicketRequest(
            "Change", "Cancel test change", "Change opened to verify cancellation.", null, null, null, null, null, null,
            null, "Low risk.", "test", DateTimeOffset.UtcNow.AddHours(4), DateTimeOffset.UtcNow.AddHours(5), null));
        cancellable.EnsureSuccessStatusCode();
        var cancelTarget = (await cancellable.Content.ReadFromJsonAsync<TicketDto>())!;
        var cancel = await admin.PostAsync($"/api/tickets/{cancelTarget.Id}/cancel", null);
        cancel.EnsureSuccessStatusCode();
        var cancelled = (await cancel.Content.ReadFromJsonAsync<TicketDto>())!;
        Assert.Equal("Cancelled", cancelled.Status);
        Assert.Contains(cancelled.Activities, a => a.Kind == "cancelled");

        var cancelledDecision = await admin.PostAsJsonAsync($"/api/tickets/{cancelTarget.Id}/decision", new DecideTicketRequest("Approved", null));
        Assert.Equal(HttpStatusCode.Conflict, cancelledDecision.StatusCode);
    }

    [Fact]
    public async Task TicketOperationsEnforceRolePolicy()
    {
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var users = await SeedUsersAsync(suffix, UserRole.Operator, UserRole.Viewer, UserRole.Approver);
        using var operatorClient = await CreateAuthorizedClientAsync(users[UserRole.Operator], Password);
        using var viewer = await CreateAuthorizedClientAsync(users[UserRole.Viewer], Password);
        using var approver = await CreateAuthorizedClientAsync(users[UserRole.Approver], Password);

        var createBody = new CreateTicketRequest("Incident", "Role policy", "Created to verify role policy.", null, null, null, null, null, null, null, null, null, null, null, null);

        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsJsonAsync("/api/tickets", createBody)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await approver.PostAsJsonAsync("/api/tickets", createBody)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync("/api/tickets")).StatusCode);

        var ticket = await CreateWorkTicketAsync(operatorClient, "Incident", "Operator ticket", "Created by an operator.");

        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PutAsJsonAsync($"/api/tickets/{ticket.Id}/status", new TransitionTicketRequest("Acknowledged", null, null))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await approver.PutAsJsonAsync($"/api/tickets/{ticket.Id}/status", new TransitionTicketRequest("Acknowledged", null, null))).StatusCode);
        var acknowledge = await operatorClient.PutAsJsonAsync($"/api/tickets/{ticket.Id}/status", new TransitionTicketRequest("Acknowledged", null, null));
        acknowledge.EnsureSuccessStatusCode();

        var change = await CreateChangeTicketAsync(operatorClient, "Operator change", "Change created by an operator.");
        var operatorDecision = await operatorClient.PostAsJsonAsync($"/api/tickets/{change.Id}/decision", new DecideTicketRequest("Approved", null));
        Assert.Equal(HttpStatusCode.Forbidden, operatorDecision.StatusCode);
        var viewerDecision = await viewer.PostAsJsonAsync($"/api/tickets/{change.Id}/decision", new DecideTicketRequest("Approved", null));
        Assert.Equal(HttpStatusCode.Forbidden, viewerDecision.StatusCode);
        var approverDecision = await approver.PostAsJsonAsync($"/api/tickets/{change.Id}/decision", new DecideTicketRequest("Rejected", "Not approved."));
        approverDecision.EnsureSuccessStatusCode();
        Assert.Equal("Rejected", (await approverDecision.Content.ReadFromJsonAsync<TicketDto>())!.Status);
    }

    [Fact]
    public async Task TicketAccessIsIsolatedBetweenTenants()
    {
        using var admin = await CreateAuthorizedClientAsync(Support360ApiFactory.AdminEmail, Support360ApiFactory.AdminPassword);
        var ticket = await CreateWorkTicketAsync(admin, "Incident", "Isolation", "Ticket used for tenant isolation checks.");
        var change = await CreateChangeTicketAsync(admin, "Isolation change", "Change used for tenant isolation checks.");

        using var otherTenant = await CreateOtherTenantClientAsync();
        Assert.Empty(await otherTenant.GetFromJsonAsync<List<TicketDto>>("/api/tickets") ?? []);
        Assert.Equal(HttpStatusCode.NotFound, (await otherTenant.GetAsync($"/api/tickets/{ticket.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await otherTenant.PutAsJsonAsync($"/api/tickets/{ticket.Id}/status", new TransitionTicketRequest("Acknowledged", null, null))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await otherTenant.PostAsJsonAsync($"/api/tickets/{change.Id}/decision", new DecideTicketRequest("Approved", null))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await otherTenant.PostAsync($"/api/tickets/{change.Id}/cancel", null)).StatusCode);
    }

    [Fact]
    public async Task TicketCollaborationSupportsCommentsWorklogsAndWatchers()
    {
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var users = await SeedUsersAsync(suffix, UserRole.Operator, UserRole.Viewer);
        using var operatorClient = await CreateAuthorizedClientAsync(users[UserRole.Operator], Password);
        using var viewer = await CreateAuthorizedClientAsync(users[UserRole.Viewer], Password);
        var ticket = await CreateWorkTicketAsync(operatorClient, "Incident", "Collaboration", "Ticket used to verify collaboration.");

        var publicComment = await viewer.PostAsJsonAsync($"/api/tickets/{ticket.Id}/comments", new AddTicketCommentRequest("Customer-visible update.", false));
        publicComment.EnsureSuccessStatusCode();

        var internalAsViewer = await viewer.PostAsJsonAsync($"/api/tickets/{ticket.Id}/comments", new AddTicketCommentRequest("Internal triage.", true));
        Assert.Equal(HttpStatusCode.Forbidden, internalAsViewer.StatusCode);

        var internalComment = await operatorClient.PostAsJsonAsync($"/api/tickets/{ticket.Id}/comments", new AddTicketCommentRequest("Internal triage.", true));
        internalComment.EnsureSuccessStatusCode();
        var withInternal = (await internalComment.Content.ReadFromJsonAsync<TicketDto>())!;
        Assert.Contains(withInternal.Comments, c => c.IsInternal && c.Body == "Internal triage.");

        var worklog = await operatorClient.PostAsJsonAsync($"/api/tickets/{ticket.Id}/worklogs", new AddTicketWorklogRequest(45, "Diagnosed the issue."));
        worklog.EnsureSuccessStatusCode();
        var withWorklog = (await worklog.Content.ReadFromJsonAsync<TicketDto>())!;
        Assert.Contains(withWorklog.Worklogs, w => w.Minutes == 45 && w.Note == "Diagnosed the issue.");

        var worklogAsViewer = await viewer.PostAsJsonAsync($"/api/tickets/{ticket.Id}/worklogs", new AddTicketWorklogRequest(15, null));
        Assert.Equal(HttpStatusCode.Forbidden, worklogAsViewer.StatusCode);

        var watch = await viewer.PostAsync($"/api/tickets/{ticket.Id}/watch", null);
        watch.EnsureSuccessStatusCode();
        Assert.Single((await watch.Content.ReadFromJsonAsync<TicketDto>())!.Watchers);

        var unwatch = await viewer.DeleteAsync($"/api/tickets/{ticket.Id}/watch");
        unwatch.EnsureSuccessStatusCode();
        Assert.Empty((await unwatch.Content.ReadFromJsonAsync<TicketDto>())!.Watchers);

        var viewerDetail = (await viewer.GetFromJsonAsync<TicketDto>($"/api/tickets/{ticket.Id}"))!;
        Assert.Contains(viewerDetail.Comments, c => c.Body == "Customer-visible update.");
        Assert.DoesNotContain(viewerDetail.Comments, c => c.IsInternal);

        var operatorDetail = (await operatorClient.GetFromJsonAsync<TicketDto>($"/api/tickets/{ticket.Id}"))!;
        Assert.Contains(operatorDetail.Comments, c => c.IsInternal && c.Body == "Internal triage.");
        Assert.Contains(operatorDetail.Activities, a => a.Kind == "comment_added");
        Assert.Contains(operatorDetail.Activities, a => a.Kind == "worklog_added");

        Assert.Equal(HttpStatusCode.BadRequest, (await operatorClient.PostAsJsonAsync($"/api/tickets/{ticket.Id}/comments", new AddTicketCommentRequest("", false))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await operatorClient.PostAsJsonAsync($"/api/tickets/{ticket.Id}/worklogs", new AddTicketWorklogRequest(0, null))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await operatorClient.PostAsync($"/api/tickets/{Guid.NewGuid()}/watch", null)).StatusCode);
    }

    [Fact]
    public async Task TicketDueDateCanBeSetValidatedAndCleared()
    {
        using var admin = await CreateAuthorizedClientAsync(Support360ApiFactory.AdminEmail, Support360ApiFactory.AdminPassword);
        var ticket = await CreateWorkTicketAsync(admin, "Incident", "Due date", "Ticket used to verify due-date updates.");
        Assert.Null(ticket.DueAt);

        var due = DateTimeOffset.UtcNow.AddDays(3);
        var set = await admin.PutAsJsonAsync($"/api/tickets/{ticket.Id}/due-date", new SetTicketDueDateRequest(due));
        set.EnsureSuccessStatusCode();
        var withDue = (await set.Content.ReadFromJsonAsync<TicketDto>())!;
        Assert.NotNull(withDue.DueAt);
        Assert.Contains(withDue.Activities, a => a.Kind == "due_date_changed");

        var past = DateTimeOffset.UtcNow.AddDays(-1);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync($"/api/tickets/{ticket.Id}/due-date", new SetTicketDueDateRequest(past))).StatusCode);

        var clear = await admin.PutAsJsonAsync($"/api/tickets/{ticket.Id}/due-date", new SetTicketDueDateRequest(null));
        clear.EnsureSuccessStatusCode();
        Assert.Null((await clear.Content.ReadFromJsonAsync<TicketDto>())!.DueAt);

        Assert.Equal(HttpStatusCode.NotFound, (await admin.PutAsJsonAsync($"/api/tickets/{Guid.NewGuid()}/due-date", new SetTicketDueDateRequest(due))).StatusCode);
    }

    [Fact]
    public async Task TicketAttachmentsSupportUploadDownloadAndDeletion()
    {
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var users = await SeedUsersAsync(suffix, UserRole.Operator, UserRole.Viewer);
        using var operatorClient = await CreateAuthorizedClientAsync(users[UserRole.Operator], Password);
        using var viewer = await CreateAuthorizedClientAsync(users[UserRole.Viewer], Password);
        var ticket = await CreateWorkTicketAsync(operatorClient, "Incident", "Attachments", "Ticket used to verify attachments.");

        var bytes = Encoding.UTF8.GetBytes("evidence");
        using (var form = FileForm(bytes, "text/plain", "evidence.txt"))
        {
            var upload = await operatorClient.PostAsync($"/api/tickets/{ticket.Id}/attachments", form);
            upload.EnsureSuccessStatusCode();
            var attachment = Assert.Single((await upload.Content.ReadFromJsonAsync<TicketDto>())!.Attachments);
            Assert.Equal("evidence.txt", attachment.FileName);
            Assert.Equal(bytes.Length, attachment.SizeBytes);
            Assert.Equal(64, attachment.Sha256.Length);

            var download = await viewer.GetAsync($"/api/tickets/{ticket.Id}/attachments/{attachment.Id}");
            download.EnsureSuccessStatusCode();
            Assert.Equal(bytes, await download.Content.ReadAsByteArrayAsync());

            using (var viewerForm = FileForm(bytes, "text/plain", "viewer.txt"))
            {
                var forbidden = await viewer.PostAsync($"/api/tickets/{ticket.Id}/attachments", viewerForm);
                Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
            }

            using var otherTenant = await CreateOtherTenantClientAsync();
            Assert.Equal(HttpStatusCode.NotFound, (await otherTenant.GetAsync($"/api/tickets/{ticket.Id}/attachments/{attachment.Id}")).StatusCode);

            var delete = await operatorClient.DeleteAsync($"/api/tickets/{ticket.Id}/attachments/{attachment.Id}");
            delete.EnsureSuccessStatusCode();
            Assert.Empty((await delete.Content.ReadFromJsonAsync<TicketDto>())!.Attachments);
            Assert.Equal(HttpStatusCode.NotFound, (await viewer.GetAsync($"/api/tickets/{ticket.Id}/attachments/{attachment.Id}")).StatusCode);
        }

        using var emptyForm = FileForm([], "text/plain", "empty.txt");
        Assert.Equal(HttpStatusCode.BadRequest, (await operatorClient.PostAsync($"/api/tickets/{ticket.Id}/attachments", emptyForm)).StatusCode);
    }

    private static MultipartFormDataContent FileForm(byte[] bytes, string contentType, string fileName)
    {
        var form = new MultipartFormDataContent();
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(content, "file", fileName);
        return form;
    }

    private static async Task<TicketDto> CreateWorkTicketAsync(HttpClient client, string type, string title, string description)
    {
        var response = await client.PostAsJsonAsync("/api/tickets", new CreateTicketRequest(
            type, title, description, type == "ServiceRequest" ? "Access" : null, null, null, null, null, null, null, null, null, null, null, null));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<TicketDto>())!;
    }

    private static async Task<TicketDto> CreateChangeTicketAsync(HttpClient client, string title, string description)
    {
        var response = await client.PostAsJsonAsync("/api/tickets", new CreateTicketRequest(
            "Change", title, description, null, null, null, null, null, null, null, "Low risk.", "test",
            DateTimeOffset.UtcNow.AddHours(2), DateTimeOffset.UtcNow.AddHours(3), null));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<TicketDto>())!;
    }

    private async Task<(string Email, string Password)[]> CreateApproversAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var rows = new[]
        {
            (Email: $"approver-one-{suffix}@support360.local", Password: "Approver-One-Password-123!"),
            (Email: $"approver-two-{suffix}@support360.local", Password: "Approver-Two-Password-123!")
        };
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Support360DbContext>();
        var tenant = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var tenantId = await db.Tenants.IgnoreQueryFilters()
            .Where(t => t.Slug == "integration")
            .Select(t => t.Id)
            .SingleAsync();
        tenant.SetSystem();
        foreach (var row in rows)
            db.Users.Add(new User
            {
                TenantId = tenantId, Email = row.Email, EmailNormalized = row.Email.ToLowerInvariant(),
                DisplayName = row.Email, PasswordHash = hasher.Hash(row.Password), Role = UserRole.Approver
            });
        await db.SaveChangesAsync();
        return rows;
    }

    private async Task<Dictionary<UserRole, string>> SeedUsersAsync(string suffix, params UserRole[] roles)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Support360DbContext>();
        var tenantContext = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        tenantContext.SetSystem();
        var tenantId = await db.Tenants.IgnoreQueryFilters()
            .Where(t => t.Slug == "integration")
            .Select(t => t.Id)
            .SingleAsync();

        var emails = new Dictionary<UserRole, string>();
        foreach (var role in roles)
        {
            var email = $"ticket-{role.ToString().ToLowerInvariant()}-{suffix}@support360.local";
            db.Users.Add(new User
            {
                TenantId = tenantId,
                Email = email,
                EmailNormalized = email,
                DisplayName = $"{role} {suffix}",
                PasswordHash = hasher.Hash(Password),
                Role = role
            });
            emails[role] = email;
        }

        await db.SaveChangesAsync();
        return emails;
    }

    private async Task<HttpClient> CreateOtherTenantClientAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var email = $"ticket-other-{suffix}@support360.local";
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Support360DbContext>();
            var tenantContext = scope.ServiceProvider.GetRequiredService<ITenantContext>();
            var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
            tenantContext.SetSystem();
            var other = new Tenant { Name = $"Other Ticket Tenant {suffix}", Slug = $"other-tickets-{suffix}" };
            db.Tenants.Add(other);
            db.Users.Add(new User
            {
                TenantId = other.Id, Email = email, EmailNormalized = email.ToLowerInvariant(), DisplayName = "Other tenant admin",
                PasswordHash = hasher.Hash(Password), Role = UserRole.TenantAdmin
            });
            await db.SaveChangesAsync();
        }
        var client = _factory.CreateClient();
        var login = await LoginAsync(client, email, Password);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
        return client;
    }

    private async Task<HttpClient> CreateAuthorizedClientAsync(string email, string password)
    {
        var client = _factory.CreateClient();
        var login = await LoginAsync(client, email, password);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
        return client;
    }

    private static async Task<LoginResponse> LoginAsync(HttpClient client, string email, string password)
    {
        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, password));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<LoginResponse>())!;
    }
}
