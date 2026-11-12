using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Support360.Application.Abstractions;
using Support360.Contracts.Operations;

namespace Support360.Api.Controllers;

[ApiController]
[Route("api/tickets")]
[Authorize(Policy = "TenantUser")]
public sealed class TicketsController : TenantControllerBase
{
    private readonly ITicketService _service;
    public TicketsController(ITicketService service) => _service = service;

    [HttpGet]
    public Task<IReadOnlyList<TicketDto>> List(
        [FromQuery] string? type,
        [FromQuery] string? status,
        [FromQuery] string? priority,
        [FromQuery] Guid? assignedToUserId,
        [FromQuery] string? q,
        CancellationToken cancellationToken) =>
        _service.ListAsync(RequireTenantId(), type, status, priority, assignedToUserId, q, cancellationToken);

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<TicketDto>> Get(Guid id, CancellationToken cancellationToken)
    {
        var ticket = await _service.GetAsync(RequireActor(), id, cancellationToken);
        return ticket is null ? NotFound() : Ok(ticket);
    }

    [HttpPost]
    [Authorize(Policy = "Operator")]
    public async Task<ActionResult<TicketDto>> Create(CreateTicketRequest request, CancellationToken cancellationToken)
    {
        var ticket = await _service.CreateAsync(RequireActor(), request, cancellationToken);
        return Created($"/api/tickets/{ticket.Id}", ticket);
    }

    [HttpPut("{id:guid}/status")]
    [Authorize(Policy = "Operator")]
    public async Task<ActionResult<TicketDto>> Transition(Guid id, TransitionTicketRequest request, CancellationToken cancellationToken)
    {
        var ticket = await _service.TransitionAsync(RequireActor(), id, request, cancellationToken);
        return ticket is null ? NotFound() : Ok(ticket);
    }

    [HttpPost("{id:guid}/decision")]
    [Authorize(Policy = "Approver")]
    public async Task<ActionResult<TicketDto>> Decide(Guid id, DecideTicketRequest request, CancellationToken cancellationToken)
    {
        var ticket = await _service.DecideAsync(RequireActor(), id, request, cancellationToken);
        return ticket is null ? NotFound() : Ok(ticket);
    }

    [HttpPost("{id:guid}/cancel")]
    [Authorize(Policy = "Operator")]
    public async Task<ActionResult<TicketDto>> Cancel(Guid id, CancellationToken cancellationToken)
    {
        var ticket = await _service.CancelAsync(RequireActor(), id, cancellationToken);
        return ticket is null ? NotFound() : Ok(ticket);
    }

    [HttpPut("{id:guid}/due-date")]
    [Authorize(Policy = "Operator")]
    public async Task<ActionResult<TicketDto>> SetDueDate(Guid id, SetTicketDueDateRequest request, CancellationToken cancellationToken)
    {
        var ticket = await _service.SetDueDateAsync(RequireActor(), id, request.DueAt, cancellationToken);
        return ticket is null ? NotFound() : Ok(ticket);
    }

    [HttpPost("{id:guid}/comments")]
    public async Task<ActionResult<TicketDto>> AddComment(Guid id, AddTicketCommentRequest request, CancellationToken cancellationToken)
    {
        var ticket = await _service.AddCommentAsync(RequireActor(), id, request, cancellationToken);
        return ticket is null ? NotFound() : Ok(ticket);
    }

    [HttpPost("{id:guid}/worklogs")]
    [Authorize(Policy = "Operator")]
    public async Task<ActionResult<TicketDto>> AddWorklog(Guid id, AddTicketWorklogRequest request, CancellationToken cancellationToken)
    {
        var ticket = await _service.AddWorklogAsync(RequireActor(), id, request, cancellationToken);
        return ticket is null ? NotFound() : Ok(ticket);
    }

    [HttpPost("{id:guid}/watch")]
    public async Task<ActionResult<TicketDto>> Watch(Guid id, CancellationToken cancellationToken)
    {
        var ticket = await _service.SetWatchingAsync(RequireActor(), id, true, cancellationToken);
        return ticket is null ? NotFound() : Ok(ticket);
    }

    [HttpDelete("{id:guid}/watch")]
    public async Task<ActionResult<TicketDto>> Unwatch(Guid id, CancellationToken cancellationToken)
    {
        var ticket = await _service.SetWatchingAsync(RequireActor(), id, false, cancellationToken);
        return ticket is null ? NotFound() : Ok(ticket);
    }

    [HttpPost("{id:guid}/attachments")]
    [Authorize(Policy = "Operator")]
    [RequestSizeLimit(TicketAttachmentLimits.MaximumBytes + 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = TicketAttachmentLimits.MaximumBytes + 1024 * 1024)]
    public async Task<ActionResult<TicketDto>> UploadAttachment(
        Guid id,
        [FromForm] IFormFile file,
        CancellationToken cancellationToken)
    {
        if (file is null || file.Length is <= 0 or > TicketAttachmentLimits.MaximumBytes)
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: "Invalid attachment size", detail: "Attachments must be between 1 byte and 10 MiB.");

        await using var stream = file.OpenReadStream();
        using var memory = new MemoryStream((int)file.Length);
        await stream.CopyToAsync(memory, cancellationToken);
        var ticket = await _service.AddAttachmentAsync(
            RequireActor(), id,
            new UploadTicketAttachmentRequest(file.FileName, file.ContentType, memory.ToArray()),
            cancellationToken);
        return ticket is null ? NotFound() : Ok(ticket);
    }

    [HttpGet("{id:guid}/attachments/{attachmentId:guid}")]
    public async Task<IActionResult> DownloadAttachment(Guid id, Guid attachmentId, CancellationToken cancellationToken)
    {
        var attachment = await _service.GetAttachmentAsync(RequireTenantId(), id, attachmentId, cancellationToken);
        return attachment is null
            ? NotFound()
            : File(attachment.Content, "application/octet-stream", attachment.FileName);
    }

    [HttpDelete("{id:guid}/attachments/{attachmentId:guid}")]
    [Authorize(Policy = "Operator")]
    public async Task<ActionResult<TicketDto>> DeleteAttachment(Guid id, Guid attachmentId, CancellationToken cancellationToken)
    {
        var ticket = await _service.DeleteAttachmentAsync(RequireActor(), id, attachmentId, cancellationToken);
        return ticket is null ? NotFound() : Ok(ticket);
    }
}
