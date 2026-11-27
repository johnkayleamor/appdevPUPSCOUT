using System.ComponentModel.DataAnnotations;

namespace appdevPUPSCOUT.Models;

public class OtpCode
{
    public int Id { get; set; }

    [Required]
    public string Email { get; set; } = string.Empty;

    [Required]
    public string Purpose { get; set; } = string.Empty;

    [Required]
    public string CodeHash { get; set; } = string.Empty;

    [Required]
    public string Salt { get; set; } = string.Empty;

    public int Attempts { get; set; }

    public string? RequestIp { get; set; }

    public DateTime ExpiresAt { get; set; }

    public bool Used { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
