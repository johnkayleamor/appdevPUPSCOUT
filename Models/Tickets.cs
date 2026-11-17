using Microsoft.AspNetCore.Identity;

namespace appdevPUPSCOUT.Models;

public class Ticket
{
    public int Id { get; set; }
    public string TrackingNumber { get; set; } = string.Empty;
    public TicketCategory Category { get; set; }
    public int RoomId { get; set; }
    public Room? Room { get; set; }
    public int EquipmentId { get; set; }
    public Equipment? Equipment { get; set; }
    public string Description { get; set; } = string.Empty;
    public TicketStatus Status { get; set; } = TicketStatus.Pending;
    public TicketPriority Priority { get; set; } = TicketPriority.Medium;
    public string ReporterId { get; set; } = string.Empty;
    public ApplicationUser? Reporter { get; set; }
    public string? AssigneeId { get; set; }
    public ApplicationUser? Assignee { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public List<TicketPhoto> Photos { get; set; } = new();
    public List<TicketNote> Notes { get; set; } = new();
    public List<TicketStatusHistory> StatusHistory { get; set; } = new();
}
