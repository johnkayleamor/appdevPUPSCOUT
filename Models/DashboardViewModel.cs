namespace appdevPUPSCOUT.Models;

public class DashboardViewModel
{
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public List<Ticket> Tickets { get; set; } = new();
    public Dictionary<TicketStatus, int> Counts { get; set; } = new();
    public Dictionary<TicketStatus, int> StatusCounts { get; set; } = new();
    public int TotalCount { get; set; }
}
