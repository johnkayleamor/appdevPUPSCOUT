namespace appdevPUPSCOUT.Models;

public class TicketStatusHistory
{
    public int Id { get; set; }
    public int TicketId { get; set; }
    public Ticket? Ticket { get; set; }
    public TicketStatus Status { get; set; }
    public string? ChangedByUserId { get; set; }
    public ApplicationUser? ChangedBy { get; set; }
    public DateTime ChangedAt { get; set; } = DateTime.UtcNow;
}