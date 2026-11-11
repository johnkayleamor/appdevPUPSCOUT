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

        if (days <= 0)
        {
            TempData["Error"] = "Suspension days must be greater than zero.";
            return RedirectToAction(nameof(Users));
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

    public IActionResult Locations()
    {
        ViewBag.Buildings = _dbContext.Buildings.OrderBy(b => b.Name).ToList();
        ViewBag.Floors = _dbContext.Floors.Include(f => f.Building).OrderBy(f => f.Name).ToList();
        ViewBag.Rooms = _dbContext.Rooms.Include(r => r.Floor).ThenInclude(f => f.Building).OrderBy(r => r.Name).ToList();
        ViewBag.Equipment = _dbContext.Equipment.Include(e => e.Room).OrderBy(e => e.Name).ToList();
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateBuilding(string name)
    {
        if (!string.IsNullOrWhiteSpace(name))
        {
            _dbContext.Buildings.Add(new Building { Name = name.Trim() });
            await _dbContext.SaveChangesAsync();
        }
        return RedirectToAction(nameof(Locations));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditBuilding(int id, string name)
    {
        var building = await _dbContext.Buildings.FindAsync(id);
        if (building is not null && !string.IsNullOrWhiteSpace(name))
        {
            building.Name = name.Trim();
            await _dbContext.SaveChangesAsync();
        }
        return RedirectToAction(nameof(Locations));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteBuilding(int id)
    {
        var building = await _dbContext.Buildings.Include(b => b.Floors).FirstOrDefaultAsync(b => b.Id == id);
        if (building is not null)
        {
            _dbContext.Buildings.Remove(building);
            try { await _dbContext.SaveChangesAsync(); }
            catch { TempData["Error"] = "Cannot delete building in use."; }
        }
        return RedirectToAction(nameof(Locations));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateFloor(string name, int buildingId)
    {
        if (!string.IsNullOrWhiteSpace(name) && buildingId > 0)
        {
            _dbContext.Floors.Add(new Floor { Name = name.Trim(), BuildingId = buildingId });
            await _dbContext.SaveChangesAsync();
        }
        return RedirectToAction(nameof(Locations));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditFloor(int id, string name, int buildingId)
    {
        var floor = await _dbContext.Floors.FindAsync(id);
        if (floor is not null && !string.IsNullOrWhiteSpace(name) && buildingId > 0)
        {
            floor.Name = name.Trim();
            floor.BuildingId = buildingId;
            await _dbContext.SaveChangesAsync();
        }
        return RedirectToAction(nameof(Locations));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteFloor(int id)
    {
        var floor = await _dbContext.Floors.Include(f => f.Rooms).FirstOrDefaultAsync(f => f.Id == id);
        if (floor is not null)
        {
            _dbContext.Floors.Remove(floor);
            try { await _dbContext.SaveChangesAsync(); }
            catch { TempData["Error"] = "Cannot delete floor in use."; }
        }
        return RedirectToAction(nameof(Locations));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateRoom(string name, int floorId, bool isActive)
    {
        if (!string.IsNullOrWhiteSpace(name) && floorId > 0)
        {
            _dbContext.Rooms.Add(new Room { Name = name.Trim(), FloorId = floorId, IsActive = isActive });
            await _dbContext.SaveChangesAsync();
        }
        return RedirectToAction(nameof(Locations));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditRoom(int id, string name, int floorId, bool isActive)
    {
        var room = await _dbContext.Rooms.FindAsync(id);
        if (room is not null && !string.IsNullOrWhiteSpace(name) && floorId > 0)
        {
            room.Name = name.Trim();
            room.FloorId = floorId;
            room.IsActive = isActive;
            await _dbContext.SaveChangesAsync();
        }
        return RedirectToAction(nameof(Locations));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteRoom(int id)
    {
        var room = await _dbContext.Rooms.Include(r => r.Equipment).FirstOrDefaultAsync(r => r.Id == id);
        if (room is not null)
        {
            _dbContext.Rooms.Remove(room);
            try { await _dbContext.SaveChangesAsync(); }
            catch { TempData["Error"] = "Cannot delete room in use."; }
        }
        return RedirectToAction(nameof(Locations));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateEquipment(string name, int roomId)
    {
        if (!string.IsNullOrWhiteSpace(name) && roomId > 0)
        {
            _dbContext.Equipment.Add(new Equipment { Name = name.Trim(), RoomId = roomId });
            await _dbContext.SaveChangesAsync();
        }
        return RedirectToAction(nameof(Locations));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditEquipment(int id, string name, int roomId)
    {
        var eq = await _dbContext.Equipment.FindAsync(id);
        if (eq is not null && !string.IsNullOrWhiteSpace(name) && roomId > 0)
        {
            eq.Name = name.Trim();
            eq.RoomId = roomId;
            await _dbContext.SaveChangesAsync();
        }
        return RedirectToAction(nameof(Locations));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteEquipment(int id)
    {
        var eq = await _dbContext.Equipment.FindAsync(id);
        if (eq is not null)
        {
            _dbContext.Equipment.Remove(eq);
            try { await _dbContext.SaveChangesAsync(); }
            catch { TempData["Error"] = "Cannot delete equipment in use."; }
        }
        return RedirectToAction(nameof(Locations));
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
