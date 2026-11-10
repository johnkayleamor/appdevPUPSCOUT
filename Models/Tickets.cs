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
    public string ReporterId { get; set; } = string.Empty;
    public ApplicationUser? Reporter { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
