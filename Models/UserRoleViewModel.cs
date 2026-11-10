namespace appdevPUPSCOUT.Models;

public class UserRoleViewModel
{
    public string UserId { get; set; } = string.Empty;
    public string? Email { get; set; }
    public IList<string> Roles { get; set; } = new List<string>();
    public IEnumerable<string> AllRoles { get; set; } = Array.Empty<string>();
    public string SelectedRole { get; set; } = string.Empty;
    public bool IsDeactivated { get; set; }
    public DateTimeOffset? LockoutEnd { get; set; }
    public int InvalidReportCount { get; set; }
}
