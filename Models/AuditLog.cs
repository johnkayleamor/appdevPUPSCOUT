namespace appdevPUPSCOUT.Models;

public class AuditLog
{
    public int Id { get; set; }
    public string? TargetUserId { get; set; }
    public string? TargetUserEmail { get; set; }
    public string? AdminId { get; set; }
    public string? AdminName { get; set; }
    public string Action { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
