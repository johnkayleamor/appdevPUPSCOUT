using appdevPUPSCOUT.Data;
using appdevPUPSCOUT.Models;
using Microsoft.AspNetCore.Identity;

namespace appdevPUPSCOUT.Services;

public class TicketWorkflowService(ApplicationDbContext dbContext, TrackingNumberGenerator trackingNumberGenerator, RoutingService routingService, PriorityService priorityService, AuditService auditService, NotificationService notificationService, UserManager<ApplicationUser> userManager)
{
    private readonly ApplicationDbContext _dbContext = dbContext;
    private readonly TrackingNumberGenerator _trackingNumberGenerator = trackingNumberGenerator;
    private readonly RoutingService _routingService = routingService;
    private readonly PriorityService _priorityService = priorityService;
    private readonly AuditService _auditService = auditService;
    private readonly NotificationService _notificationService = notificationService;
    private readonly UserManager<ApplicationUser> _userManager = userManager;

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

        var admins = await _userManager.GetUsersInRoleAsync("Admin");
        foreach (var admin in admins)
        {
            if (admin.Id == reporter.Id) continue;
            await _notificationService.NotifyAsync(admin.Id, ticket.Id, ticket.TrackingNumber,
                $"New report {ticket.TrackingNumber} submitted.", $"/Tickets/Details/{ticket.Id}");
        }
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

        if (newStatus == TicketStatus.PendingVerification)
        {
            ticket.VerificationStartedAt = DateTime.UtcNow;
        }
        else if (newStatus == TicketStatus.InProgress)
        {
            ticket.VerificationStartedAt = null;
        }

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

        if (userId != ticket.ReporterId)
        {
            await _notificationService.NotifyAsync(ticket.ReporterId, ticket.Id, ticket.TrackingNumber,
                $"Ticket {ticket.TrackingNumber} status changed to {newStatus}.", $"/Tickets/Details/{ticket.Id}");
        }
        return (true, null);
    }

    public async Task<(bool Success, string? Error)> ConfirmAsync(int ticketId, string userId, bool isAdmin, string? reason)
    {
        var ticket = await _dbContext.Tickets.FindAsync(ticketId);
        if (ticket is null) return (false, "Ticket not found.");

        if (ticket.Status != TicketStatus.PendingVerification)
        {
            return (false, "Ticket is not pending verification.");
        }

        if (isAdmin && string.IsNullOrWhiteSpace(reason))
        {
            return (false, "A reason is required.");
        }

        ticket.Status = TicketStatus.Closed;
        ticket.ClosureType = isAdmin ? TicketClosureType.ClosedByAdmin : TicketClosureType.Confirmed;

        _dbContext.TicketStatusHistories.Add(new TicketStatusHistory
        {
            TicketId = ticket.Id,
            Status = TicketStatus.Closed,
            ChangedByUserId = userId,
            ChangedAt = DateTime.UtcNow
        });

        if (!string.IsNullOrWhiteSpace(reason))
        {
            _dbContext.TicketNotes.Add(new TicketNote
            {
                TicketId = ticket.Id,
                AuthorId = userId,
                Note = reason!.Trim()
            });
        }

        await _auditService.Log(userId, isAdmin ? "ConfirmByAdmin" : "ConfirmFix", ticket.Id,
            $"Ticket {ticket.TrackingNumber} confirmed and closed.",
            ticket.ReporterId, null, null);

        if (userId != ticket.ReporterId)
        {
            await _notificationService.NotifyAsync(ticket.ReporterId, ticket.Id, ticket.TrackingNumber,
                $"Ticket {ticket.TrackingNumber} was closed.", $"/Tickets/Details/{ticket.Id}");
        }
        return (true, null);
    }

    public async Task<(bool Success, string? Error)> RejectAsync(int ticketId, string userId, string reason, bool isAdmin)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return (false, "A reason is required to reject the fix.");
        }

        var ticket = await _dbContext.Tickets.FindAsync(ticketId);
        if (ticket is null) return (false, "Ticket not found.");

        if (ticket.Status != TicketStatus.PendingVerification)
        {
            return (false, "Ticket is not pending verification.");
        }

        ticket.Status = TicketStatus.InProgress;
        ticket.VerificationStartedAt = null;
        ticket.RejectReason = reason.Trim();

        _dbContext.TicketStatusHistories.Add(new TicketStatusHistory
        {
            TicketId = ticket.Id,
            Status = TicketStatus.InProgress,
            ChangedByUserId = userId,
            ChangedAt = DateTime.UtcNow
        });

        _dbContext.TicketNotes.Add(new TicketNote
        {
            TicketId = ticket.Id,
            AuthorId = userId,
            Note = $"Rejected fix: {reason.Trim()}"
        });

        _dbContext.TicketComments.Add(new TicketComment
        {
            TicketId = ticket.Id,
            AuthorId = userId,
            AuthorRole = isAdmin ? "Admin" : "Reporter",
            Content = $"Rejected fix: {reason.Trim()}",
            CreatedAt = DateTime.UtcNow
        });

        await _auditService.Log(userId, isAdmin ? "RejectByAdmin" : "RejectFix", ticket.Id,
            $"Ticket {ticket.TrackingNumber} rejected. Reason: {reason.Trim()}",
            ticket.ReporterId, null, null);

        await _notificationService.NotifyAsync(ticket.ReporterId, ticket.Id, ticket.TrackingNumber,
            $"Ticket {ticket.TrackingNumber} was rejected and returned to In Progress.", $"/Tickets/Details/{ticket.Id}");
        if (!string.IsNullOrWhiteSpace(ticket.AssigneeId) && ticket.AssigneeId != userId)
        {
            await _notificationService.NotifyAsync(ticket.AssigneeId, ticket.Id, ticket.TrackingNumber,
                $"Ticket {ticket.TrackingNumber} was rejected. Reason: {reason.Trim()}", $"/Tickets/Details/{ticket.Id}");
        }
        return (true, null);
    }

    public async Task<(bool Success, string? Error)> AddCommentAsync(int ticketId, string userId, string content)
    {
        var ticket = await _dbContext.Tickets.FindAsync(ticketId);
        if (ticket is null) return (false, "Ticket not found.");

        if (ticket.Status == TicketStatus.Closed || ticket.Status == TicketStatus.InvalidSpam)
        {
            return (false, "Comments are read-only for this ticket.");
        }

        if (string.IsNullOrWhiteSpace(content) || content.Trim().Length > 2000)
        {
            return (false, "A comment is required and must be 2000 characters or fewer.");
        }

        var user = await _userManager.FindByIdAsync(userId);
        if (user is null) return (false, "User not found.");

        var roles = await _userManager.GetRolesAsync(user);
        _dbContext.TicketComments.Add(new TicketComment
        {
            TicketId = ticket.Id,
            AuthorId = user.Id,
            AuthorRole = roles.FirstOrDefault() ?? "User",
            Content = content.Trim(),
            CreatedAt = DateTime.UtcNow
        });

        await _dbContext.SaveChangesAsync();

        var recipients = new HashSet<string>();
        if (ticket.ReporterId != user.Id) recipients.Add(ticket.ReporterId);
        if (!string.IsNullOrWhiteSpace(ticket.AssigneeId) && ticket.AssigneeId != user.Id) recipients.Add(ticket.AssigneeId!);

        foreach (var recipientId in recipients)
        {
            await _notificationService.NotifyAsync(recipientId, ticket.Id, ticket.TrackingNumber,
                $"New comment on ticket {ticket.TrackingNumber}.", $"/Tickets/Details/{ticket.Id}");
        }

        return (true, null);
    }

    public async Task NotifyAssignmentAsync(Ticket ticket, ApplicationUser? assignee, string? previousAssigneeId, string? actorId)
    {
        if (assignee is null) return;
        if (assignee.Id == actorId) return;

        await _notificationService.NotifyAsync(assignee.Id, ticket.Id, ticket.TrackingNumber,
            $"Ticket {ticket.TrackingNumber} was assigned to you.", $"/Tickets/Details/{ticket.Id}");
    }

    public async Task AutoCloseAsync(int ticketId)
    {
        var ticket = await _dbContext.Tickets.FindAsync(ticketId);
        if (ticket is null || ticket.Status != TicketStatus.PendingVerification) return;

        ticket.Status = TicketStatus.Closed;
        ticket.ClosureType = TicketClosureType.AutoClosed;

        _dbContext.TicketStatusHistories.Add(new TicketStatusHistory
        {
            TicketId = ticket.Id,
            Status = TicketStatus.Closed,
            ChangedByUserId = null,
            ChangedAt = DateTime.UtcNow
        });

        await _auditService.Log(null, "AutoClose", ticket.Id,
            $"Ticket {ticket.TrackingNumber} auto-closed after time limit.",
            ticket.ReporterId, null, "System");

        await _notificationService.NotifyAsync(ticket.ReporterId, ticket.Id, ticket.TrackingNumber,
            $"Ticket {ticket.TrackingNumber} was auto-closed.", $"/Tickets/Details/{ticket.Id}");
    }
}