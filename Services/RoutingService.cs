using Microsoft.AspNetCore.Identity;
using appdevPUPSCOUT.Data;
using appdevPUPSCOUT.Models;

namespace appdevPUPSCOUT.Services;

public class RoutingService(UserManager<ApplicationUser> userManager, ApplicationDbContext dbContext, AuditService auditService)
{
    private readonly UserManager<ApplicationUser> _userManager = userManager;
    private readonly ApplicationDbContext _dbContext = dbContext;
    private readonly AuditService _auditService = auditService;

    public async Task<ApplicationUser?> GetAssigneeAsync(TicketCategory category)
    {
        var role = category == TicketCategory.Hardware ? "ITPersonnel" : "MaintenancePersonnel";
        var users = await _userManager.GetUsersInRoleAsync(role);
        return users.OrderBy(u => u.Email).FirstOrDefault();
    }

    public async Task AssignAsync(Ticket ticket)
    {
        if (ticket.AssigneeId is not null) return;

        var assignee = await GetAssigneeAsync(ticket.Category);
        if (assignee is null) return;

        ticket.AssigneeId = assignee.Id;
        ticket.Status = TicketStatus.Assigned;
        await _dbContext.SaveChangesAsync();

        _dbContext.TicketStatusHistories.Add(new TicketStatusHistory
        {
            TicketId = ticket.Id,
            Status = TicketStatus.Assigned,
            ChangedByUserId = null,
            ChangedAt = DateTime.UtcNow
        });
        await _dbContext.SaveChangesAsync();

        await _auditService.Log(null, "AutoAssign", ticket.Id,
            $"Ticket {ticket.TrackingNumber} auto-assigned to {assignee.Email}.",
            ticket.ReporterId, ticket.Reporter?.Email, "System");
    }
}