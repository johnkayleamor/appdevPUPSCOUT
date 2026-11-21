using appdevPUPSCOUT.Data;
using appdevPUPSCOUT.Models;
using Microsoft.AspNetCore.Identity;

namespace appdevPUPSCOUT.Services;

public class TicketWorkflowService(ApplicationDbContext dbContext, TrackingNumberGenerator trackingNumberGenerator, RoutingService routingService, PriorityService priorityService, AuditService auditService)
{
    private readonly ApplicationDbContext _dbContext = dbContext;
    private readonly TrackingNumberGenerator _trackingNumberGenerator = trackingNumberGenerator;
    private readonly RoutingService _routingService = routingService;
    private readonly PriorityService _priorityService = priorityService;
    private readonly AuditService _auditService = auditService;

    public async Task<Ticket> CreateAsync(TicketCreateViewModel model, ApplicationUser reporter)
    {
        var ticket = new Ticket
        {
            TrackingNumber = _trackingNumberGenerator.Generate(),
            Category = model.Category,
            RoomId = model.RoomId,
            EquipmentId = model.EquipmentId,
            Description = model.Description,
            ReporterId = reporter.Id,
            Status = TicketStatus.Pending
        };

        ticket.Priority = await _priorityService.CalculateAsync(ticket);

        _dbContext.Tickets.Add(ticket);
        await _dbContext.SaveChangesAsync();

        _dbContext.TicketStatusHistories.Add(new TicketStatusHistory
        {
            TicketId = ticket.Id,
            Status = TicketStatus.Pending,
            ChangedByUserId = reporter.Id,
            ChangedAt = DateTime.UtcNow
        });
        await _dbContext.SaveChangesAsync();

        await _routingService.AssignAsync(ticket);
        await _auditService.Log(reporter.Id, "Create", ticket.Id, $"Ticket {ticket.TrackingNumber} created.", reporter.Id, reporter.Email, reporter.Email);
        return ticket;
    }

    public async Task<(bool Success, string? Error)> ChangeStatusAsync(int ticketId, TicketStatus newStatus, string userId, string? note)
    {
        var ticket = await _dbContext.Tickets.FindAsync(ticketId);
        if (ticket is null)
        {
            return (false, "Ticket not found.");
        }

        if (newStatus == TicketStatus.Closed)
        {
            return (false, "Staff cannot close tickets.");
        }

        var oldStatus = ticket.Status;

        var allowed = (ticket.Status == TicketStatus.Assigned && newStatus == TicketStatus.InProgress) ||
                      (ticket.Status == TicketStatus.InProgress && newStatus == TicketStatus.PendingVerification);

        if (!allowed)
        {
            return (false, $"Cannot move from {ticket.Status} to {newStatus}.");
        }

        ticket.Status = newStatus;

        _dbContext.TicketStatusHistories.Add(new TicketStatusHistory
        {
            TicketId = ticket.Id,
            Status = newStatus,
            ChangedByUserId = userId,
            ChangedAt = DateTime.UtcNow
        });

        if (!string.IsNullOrWhiteSpace(note))
        {
            _dbContext.TicketNotes.Add(new TicketNote
            {
                TicketId = ticket.Id,
                AuthorId = userId,
                Note = note.Trim()
            });
        }

        await _auditService.Log(userId, "StatusChange", ticket.Id,
            $"Ticket {ticket.TrackingNumber}: {oldStatus} -> {newStatus}. {note}".Trim(),
            ticket.ReporterId, null, null);
        return (true, null);
    }
}