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

    public async Task<IActionResult> Tickets(string? status, string? priority, string? category, string? assigneeId, DateTime? from, DateTime? to, string? search, int page = 1)
    {
        const int pageSize = 20;

        var query = _dbContext.Tickets
            .AsNoTracking()
            .Include(t => t.Room).ThenInclude(r => r!.Floor).ThenInclude(f => f!.Building)
            .Include(t => t.Equipment)
            .Include(t => t.Reporter)
            .Include(t => t.Assignee)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<TicketStatus>(status, true, out var st))
        {
            query = query.Where(t => t.Status == st);
        }
        if (!string.IsNullOrWhiteSpace(priority) && Enum.TryParse<TicketPriority>(priority, true, out var pr))
        {
            query = query.Where(t => t.Priority == pr);
        }
        if (!string.IsNullOrWhiteSpace(category) && Enum.TryParse<TicketCategory>(category, true, out var cat))
        {
            query = query.Where(t => t.Category == cat);
        }
        if (!string.IsNullOrWhiteSpace(assigneeId))
        {
            if (assigneeId == "unassigned")
            {
                query = query.Where(t => t.AssigneeId == null);
            }
            else
            {
                query = query.Where(t => t.AssigneeId == assigneeId);
            }
        }
        if (from.HasValue)
        {
            query = query.Where(t => t.CreatedAt >= from.Value);
        }
        if (to.HasValue)
        {
            query = query.Where(t => t.CreatedAt <= to.Value.AddDays(1));
        }
        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(t => t.TrackingNumber.Contains(search) ||
                (t.Room != null && t.Room.Name.Contains(search)));
        }

        query = query
            .OrderBy(t => t.AssigneeId != null)
            .ThenByDescending(t => t.Priority)
            .ThenByDescending(t => t.CreatedAt);

        var total = await query.CountAsync();
        var tickets = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

        var staff = new List<ApplicationUser>();
        foreach (var role in new[] { "ITPersonnel", "MaintenancePersonnel" })
        {
            staff.AddRange(await _userManager.GetUsersInRoleAsync(role));
        }
        staff = staff.DistinctBy(u => u.Id).OrderBy(u => u.Email).ToList();

        ViewBag.Total = total;
        ViewBag.Page = page;
        ViewBag.PageSize = pageSize;
        ViewBag.TotalPages = (int)Math.Ceiling(total / (double)pageSize);
        ViewBag.Staff = staff;
        ViewBag.Status = status;
        ViewBag.Priority = priority;
        ViewBag.Category = category;
        ViewBag.AssigneeId = assigneeId;
        ViewBag.From = from;
        ViewBag.To = to;
        ViewBag.Search = search;

        return View(tickets);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AssignTicket(int id, string? assigneeId)
    {
        var admin = await _userManager.GetUserAsync(User);
        var ticket = await _dbContext.Tickets.FindAsync(id);
        if (ticket is null) return NotFound();

        var previousAssigneeId = ticket.AssigneeId;
        ticket.AssigneeId = string.IsNullOrWhiteSpace(assigneeId) ? null : assigneeId;
        if (!string.IsNullOrWhiteSpace(assigneeId) && ticket.Status == TicketStatus.Pending)
        {
            var oldStatus = ticket.Status;
            ticket.Status = TicketStatus.Assigned;
            _dbContext.TicketStatusHistories.Add(new TicketStatusHistory
            {
                TicketId = ticket.Id,
                Status = TicketStatus.Assigned,
                ChangedByUserId = admin?.Id,
                ChangedAt = DateTime.UtcNow
            });
        }
        await _dbContext.SaveChangesAsync();

        var assignee = string.IsNullOrWhiteSpace(assigneeId) ? null : await _userManager.FindByIdAsync(assigneeId);
        _dbContext.AuditLogs.Add(new AuditLog
        {
            TargetUserId = ticket.ReporterId,
            AdminId = admin?.Id,
            AdminName = User.Identity?.Name,
            Action = "Assign",
            Reason = $"Ticket {ticket.TrackingNumber} reassigned from {previousAssigneeId ?? "unassigned"} to {assignee?.Email ?? "unassigned"}.",
            CreatedAt = DateTime.UtcNow
        });
        await _dbContext.SaveChangesAsync();

        TempData["Message"] = "Ticket assignment updated.";
        return RedirectToAction(nameof(Tickets));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangePriority(int id, TicketPriority priority)
    {
        var ticket = await _dbContext.Tickets.FindAsync(id);
        if (ticket is null) return NotFound();

        ticket.Priority = priority;
        await _dbContext.SaveChangesAsync();
        TempData["Message"] = "Priority updated.";
        return RedirectToAction(nameof(Tickets));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkInvalid(int id)
    {
        var admin = await _userManager.GetUserAsync(User);
        var ticket = await _dbContext.Tickets.FindAsync(id);
        if (ticket is null) return NotFound();

        ticket.Status = TicketStatus.InvalidSpam;
        _dbContext.TicketStatusHistories.Add(new TicketStatusHistory
        {
            TicketId = ticket.Id,
            Status = TicketStatus.InvalidSpam,
            ChangedByUserId = admin?.Id,
            ChangedAt = DateTime.UtcNow
        });
        await _dbContext.SaveChangesAsync();
        TempData["Message"] = "Ticket marked invalid/spam.";
        return RedirectToAction(nameof(Tickets));
    }

    public async Task<IActionResult> Dashboard(DateTime? startDate, DateTime? endDate, DateTime? from, DateTime? to, DateTime? startParam, DateTime? endParam)
    {
        var startValue = startDate ?? from ?? startParam;
        var endValue = endDate ?? to ?? endParam;
        var start = (startValue ?? DateTime.UtcNow.AddDays(-30)).Date;
        var end = (endValue ?? DateTime.UtcNow).Date;

        var query = _dbContext.Tickets
            .AsNoTracking()
            .Include(t => t.Room).ThenInclude(r => r!.Floor).ThenInclude(f => f!.Building)
            .Include(t => t.Equipment)
            .Include(t => t.Reporter)
            .Include(t => t.Assignee)
            .Where(t => t.Status != TicketStatus.Closed && t.Status != TicketStatus.InvalidSpam)
            .Where(t => t.CreatedAt >= start && t.CreatedAt <= end.AddDays(1))
            .AsQueryable();

        var tickets = await query.OrderByDescending(t => t.CreatedAt).ToListAsync();

        var counts = tickets
            .GroupBy(t => t.Status)
            .ToDictionary(g => g.Key, g => g.Count());

        foreach (var status in Enum.GetValues<TicketStatus>())
        {
            if (!counts.ContainsKey(status))
            {
                counts[status] = 0;
            }
        }

        var model = new DashboardViewModel
        {
            StartDate = start,
            EndDate = end,
            Tickets = tickets,
            Counts = counts,
            StatusCounts = new Dictionary<TicketStatus, int>(counts),
            TotalCount = tickets.Count
        };

        return View(model);
    }

    public async Task<IActionResult> MostReported(DateTime? startDate, DateTime? endDate, DateTime? from, DateTime? to, DateTime? startParam, DateTime? endParam)
    {
        return await BuildMostReported(startDate, endDate, from, to, startParam, endParam);
    }

    public Task<IActionResult> TopReported(DateTime? startDate, DateTime? endDate, DateTime? from, DateTime? to, DateTime? startParam, DateTime? endParam)
        => BuildMostReported(startDate, endDate, from, to, startParam, endParam);

    public Task<IActionResult> Reports(DateTime? startDate, DateTime? endDate, DateTime? from, DateTime? to, DateTime? startParam, DateTime? endParam)
        => BuildMostReported(startDate, endDate, from, to, startParam, endParam);

    public Task<IActionResult> TopRoomsEquipment(DateTime? startDate, DateTime? endDate, DateTime? from, DateTime? to, DateTime? startParam, DateTime? endParam)
        => BuildMostReported(startDate, endDate, from, to, startParam, endParam);

    private async Task<IActionResult> BuildMostReported(DateTime? startDate, DateTime? endDate, DateTime? from, DateTime? to, DateTime? startParam, DateTime? endParam)
    {
        var startValue = startDate ?? from ?? startParam;
        var endValue = endDate ?? to ?? endParam;
        var start = (startValue ?? DateTime.UtcNow.AddDays(-30)).Date;
        var end = (endValue ?? DateTime.UtcNow).Date;

        var tickets = await _dbContext.Tickets
            .AsNoTracking()
            .Include(t => t.Room)
            .Include(t => t.Equipment)
            .Where(t => t.Status != TicketStatus.InvalidSpam)
            .Where(t => t.CreatedAt >= start && t.CreatedAt <= end.AddDays(1))
            .ToListAsync();

        var roomCounts = tickets
            .Where(t => t.Room is not null)
            .GroupBy(t => new { t.RoomId, RoomName = t.Room!.Name })
            .Select(g => new RoomReportCount
            {
                RoomId = g.Key.RoomId,
                RoomName = g.Key.RoomName,
                Count = g.Count()
            })
            .OrderByDescending(x => x.Count)
            .ThenBy(x => x.RoomName)
            .ToList();

        var equipmentCounts = tickets
            .Where(t => t.Equipment is not null)
            .GroupBy(t => new { t.EquipmentId, EquipmentName = t.Equipment!.Name })
            .Select(g => new EquipmentReportCount
            {
                EquipmentId = g.Key.EquipmentId,
                EquipmentName = g.Key.EquipmentName,
                Count = g.Count()
            })
            .OrderByDescending(x => x.Count)
            .ThenBy(x => x.EquipmentName)
            .ToList();

        var jsonOptions = new System.Text.Json.JsonSerializerOptions
        {
            PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
        };

        var roomJson = System.Text.Json.JsonSerializer.Serialize(roomCounts.Select(r => new { label = r.RoomName, value = r.Count }), jsonOptions);
        var equipmentJson = System.Text.Json.JsonSerializer.Serialize(equipmentCounts.Select(e => new { label = e.EquipmentName, value = e.Count }), jsonOptions);

        var model = new MostReportedViewModel
        {
            StartDate = start,
            EndDate = end,
            RoomCounts = roomCounts,
            EquipmentCounts = equipmentCounts,
            TopRooms = roomCounts,
            TopEquipment = equipmentCounts,
            Rooms = roomCounts,
            Equipment = equipmentCounts,
            RoomCountsJson = roomJson,
            EquipmentCountsJson = equipmentJson,
            TopRoomsJson = roomJson,
            TopEquipmentJson = equipmentJson,
            RoomsJson = roomJson,
            EquipmentJson = equipmentJson
        };

        return View("MostReported", model);
    }

    public IActionResult Locations()
    {
        ViewBag.Buildings = _dbContext.Buildings.OrderBy(b => b.Name).ToList();
        ViewBag.Floors = _dbContext.Floors.Include(f => f.Building).OrderBy(f => f.Name).ToList();
        ViewBag.Rooms = _dbContext.Rooms.Include(r => r.Floor).ThenInclude(f => f!.Building).OrderBy(r => r.Name).ToList();
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
            var hasTickets = await _dbContext.Tickets.AnyAsync(t => t.RoomId == id);
            if (hasTickets)
            {
                room.IsActive = false;
                await _dbContext.SaveChangesAsync();
                TempData["Message"] = "Room has existing tickets and was marked inactive.";
            }
            else
            {
                _dbContext.Rooms.Remove(room);
                try { await _dbContext.SaveChangesAsync(); }
                catch { TempData["Error"] = "Cannot delete room in use."; }
            }
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
