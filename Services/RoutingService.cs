using Microsoft.AspNetCore.Identity;
using appdevPUPSCOUT.Data;
using appdevPUPSCOUT.Models;

namespace appdevPUPSCOUT.Services;

public class RoutingService(UserManager<ApplicationUser> userManager, ApplicationDbContext dbContext)
{
    private readonly UserManager<ApplicationUser> _userManager = userManager;
    private readonly ApplicationDbContext _dbContext = dbContext;

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

        _dbContext.AuditLogs.Add(new AuditLog
        {
            TargetUserId = ticket.ReporterId,
            TargetUserEmail = ticket.Reporter?.Email,
            AdminId = null,
            AdminName = "System",
            Action = "AutoAssign",
            Reason = $"Ticket {ticket.TrackingNumber} auto-assigned to {assignee.Email}.",
            CreatedAt = DateTime.UtcNow
        });
        await _dbContext.SaveChangesAsync();
    }
}