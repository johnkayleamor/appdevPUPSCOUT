using Microsoft.AspNetCore.Identity;

namespace appdevPUPSCOUT.Models;

public class ApplicationUser : IdentityUser
{
    public bool IsDeactivated { get; set; }
}
