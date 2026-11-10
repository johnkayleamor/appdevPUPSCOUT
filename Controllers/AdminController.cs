using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using appdevPUPSCOUT.Data;
using appdevPUPSCOUT.Models;

namespace appdevPUPSCOUT.Controllers;

[Authorize(Roles = "Admin")]
public class AdminController(
    UserManager<ApplicationUser> userManager,
    RoleManager<IdentityRole> roleManager,
    ApplicationDbContext dbContext,
    Microsoft.AspNetCore.Identity.UI.Services.IEmailSender emailSender) : Controller
{
    private readonly UserManager<ApplicationUser> _userManager = userManager;
    private readonly RoleManager<IdentityRole> _roleManager = roleManager;
    private readonly ApplicationDbContext _dbContext = dbContext;
    private readonly Microsoft.AspNetCore.Identity.UI.Services.IEmailSender _emailSender = emailSender;

    public async Task<IActionResult> Users()
    {
        var users = _userManager.Users.OrderBy(u => u.Email).ToList();
        var model = new List<UserRoleViewModel>();

        foreach (var user in users)
        {
            var roles = await _userManager.GetRolesAsync(user);
            var invalidCount = await _dbContext.Tickets.CountAsync(t => t.ReporterId == user.Id && t.Status == TicketStatus.InvalidSpam);
            model.Add(new UserRoleViewModel
            {
                UserId = user.Id,
                Email = user.Email,
                Roles = roles,
                AllRoles = _roleManager.Roles.Select(r => r.Name!).OrderBy(n => n).ToList(),
                SelectedRole = roles.FirstOrDefault() ?? string.Empty,
                IsDeactivated = user.IsDeactivated,
                LockoutEnd = user.LockoutEnd,
                InvalidReportCount = invalidCount
            });
        }

        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangeRole(string userId, string role)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user is null)
        {
            return NotFound();
        }

        if (!await _roleManager.RoleExistsAsync(role))
        {
            return BadRequest();
        }

        var currentRoles = await _userManager.GetRolesAsync(user);
        if (currentRoles.Count > 0)
        {
            await _userManager.RemoveFromRolesAsync(user, currentRoles);
        }

        await _userManager.AddToRoleAsync(user, role);

        return RedirectToAction(nameof(Users));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Suspend(string userId, int days, string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            TempData["Error"] = "A reason is required.";
            return RedirectToAction(nameof(Users));
        }

        var user = await _userManager.FindByIdAsync(userId);
        if (user is null)
        {
            return NotFound();
        }

        var until = DateTimeOffset.UtcNow.AddDays(days);
        await _userManager.SetLockoutEndDateAsync(user, until);

        await LogAction(user, "Suspend", reason);
        await _emailSender.SendEmailAsync(user.Email!, "Account suspended",
            $"Your account has been suspended until {until:u}. Reason: {reason}. To appeal, contact admin@pup.edu.ph.");

        return RedirectToAction(nameof(Users));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Deactivate(string userId, string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            TempData["Error"] = "A reason is required.";
            return RedirectToAction(nameof(Users));
        }

        var user = await _userManager.FindByIdAsync(userId);
        if (user is null)
        {
            return NotFound();
        }

        user.IsDeactivated = true;
        await _userManager.UpdateAsync(user);
        await _userManager.SetLockoutEndDateAsync(user, DateTimeOffset.MaxValue);

        await LogAction(user, "Deactivate", reason);
        await _emailSender.SendEmailAsync(user.Email!, "Account deactivated",
            $"Your account has been deactivated. Reason: {reason}. To appeal, contact admin@pup.edu.ph.");

        return RedirectToAction(nameof(Users));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reactivate(string userId, string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            TempData["Error"] = "A reason is required.";
            return RedirectToAction(nameof(Users));
        }

        var user = await _userManager.FindByIdAsync(userId);
        if (user is null)
        {
            return NotFound();
        }

        user.IsDeactivated = false;
        await _userManager.UpdateAsync(user);
        await _userManager.SetLockoutEndDateAsync(user, null);

        await LogAction(user, "Reactivate", reason);
        await _emailSender.SendEmailAsync(user.Email!, "Account reactivated",
            $"Your account has been reactivated. Reason: {reason}.");

        return RedirectToAction(nameof(Users));
    }

    private async Task LogAction(ApplicationUser user, string action, string reason)
    {
        var admin = await _userManager.GetUserAsync(User);
        _dbContext.AuditLogs.Add(new AuditLog
        {
            TargetUserId = user.Id,
            TargetUserEmail = user.Email,
            AdminId = admin?.Id,
            AdminName = User.Identity?.Name,
            Action = action,
            Reason = reason,
            CreatedAt = DateTime.UtcNow
        });
        await _dbContext.SaveChangesAsync();
    }
}
