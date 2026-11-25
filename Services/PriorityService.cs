using appdevPUPSCOUT.Data;
using appdevPUPSCOUT.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace appdevPUPSCOUT.Services;

public class PriorityService(ApplicationDbContext dbContext, IConfiguration configuration, AuditService auditService)
{
    private readonly ApplicationDbContext _dbContext = dbContext;
    private readonly IConfiguration _configuration = configuration;
    private readonly AuditService _auditService = auditService;

    // Starting priority from category and equipment. Safety hazards start High.
    public Task<TicketPriority> CalculatePriorityAsync(Ticket ticket) => CalculateAsync(ticket);

    public async Task<TicketPriority> CalculateAsync(Ticket ticket)
    {
        var equipment = ticket.Equipment ?? await _dbContext.Equipment.FindAsync(ticket.EquipmentId);
        if (equipment is not null && equipment.IsSafetyHazard)
        {
            return TicketPriority.High;
        }

        return ticket.Category == TicketCategory.Hardware
            ? TicketPriority.Medium
            : TicketPriority.Low;
    }

    // Re-check the rule for all open tickets of the same room/equipment.
    public Task RecheckPriorityAsync(int roomId, int equipmentId) => RecheckAsync(roomId, equipmentId);
    public Task ApplyRuleAsync(int roomId, int equipmentId) => RecheckAsync(roomId, equipmentId);

    public async Task RecheckAsync(int roomId, int equipmentId)
    {
        var threshold = _configuration.GetValue<int>("Priority:OpenReportThreshold", 3);

        var openTickets = await _dbContext.Tickets
            .Include(t => t.Equipment)
            .Where(t => t.RoomId == roomId && t.EquipmentId == equipmentId &&
                        t.Status != TicketStatus.Closed && t.Status != TicketStatus.InvalidSpam)
            .ToListAsync();

        var thresholdMet = openTickets.Count >= threshold;

        foreach (var ticket in openTickets)
        {
            // Manual priorities are never overwritten by the rule.
            if (ticket.PriorityIsManual)
            {
                continue;
            }

            var equipment = ticket.Equipment ?? await _dbContext.Equipment.FindAsync(ticket.EquipmentId);
            var basePriority = await CalculateBaseAsync(ticket, equipment);

            var desired = basePriority;
            if (thresholdMet)
            {
                desired = RaiseOne(desired);
            }

            // Never go past High.
            if (desired > TicketPriority.High)
            {
                desired = TicketPriority.High;
            }

            // Never lower an existing priority.
            if (desired < ticket.Priority)
            {
                desired = ticket.Priority;
            }

            if (desired != ticket.Priority)
            {
                var oldPriority = ticket.Priority;
                ticket.Priority = desired;

                await _auditService.Log(null, "ChangePriority", ticket.Id,
                    $"Ticket {ticket.TrackingNumber} priority changed: {oldPriority} -> {desired} (rule).",
                    ticket.ReporterId, null, "Rule");
            }
        }

        await _dbContext.SaveChangesAsync();
    }

    public async Task ChangeManualAsync(Ticket ticket, TicketPriority newPriority, string adminId, string? adminName)
    {
        var oldPriority = ticket.Priority;
        ticket.Priority = newPriority;
        ticket.PriorityIsManual = true;

        await _dbContext.SaveChangesAsync();

        if (oldPriority != newPriority)
        {
            await _auditService.Log(adminId, "ChangePriority", ticket.Id,
                $"Ticket {ticket.TrackingNumber} priority changed: {oldPriority} -> {newPriority} (manual by {adminName ?? adminId}).",
                ticket.ReporterId, null, adminName);
        }
    }

    private async Task<TicketPriority> CalculateBaseAsync(Ticket ticket, Equipment? equipment)
    {
        if (equipment is not null && equipment.IsSafetyHazard)
        {
            return TicketPriority.High;
        }

        return ticket.Category == TicketCategory.Hardware
            ? TicketPriority.Medium
            : TicketPriority.Low;
    }

    private static TicketPriority RaiseOne(TicketPriority priority)
    {
        return priority switch
        {
            TicketPriority.Low => TicketPriority.Medium,
            TicketPriority.Medium => TicketPriority.High,
            _ => TicketPriority.High
        };
    }
}
