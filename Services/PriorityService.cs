using appdevPUPSCOUT.Data;
using appdevPUPSCOUT.Models;
using Microsoft.EntityFrameworkCore;

namespace appdevPUPSCOUT.Services;

public class PriorityService(ApplicationDbContext dbContext)
{
    private readonly ApplicationDbContext _dbContext = dbContext;

    public async Task<TicketPriority> CalculateAsync(Ticket ticket)
    {
        var openStatuses = new[] { TicketStatus.Pending, TicketStatus.Assigned, TicketStatus.InProgress, TicketStatus.PendingVerification };
        var similarOpen = await _dbContext.Tickets.CountAsync(t =>
            t.RoomId == ticket.RoomId &&
            t.EquipmentId == ticket.EquipmentId &&
            openStatuses.Contains(t.Status) &&
            t.Id != ticket.Id);

        if (similarOpen > 0)
        {
            return TicketPriority.High;
        }

        return TicketPriority.Medium;
    }
}