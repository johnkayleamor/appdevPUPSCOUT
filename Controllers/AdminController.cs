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
    Microsoft.AspNetCore.Identity.UI.Services.IEmailSender emailSender,
    Services.AuditService auditService,
    Services.TicketWorkflowService ticketWorkflowService,
    Services.NotificationService notificationService,
    Services.LookupCacheService lookupCacheService,
    IConfiguration configuration,
    ILogger<AdminController> logger) : Controller
{
    private readonly UserManager<ApplicationUser> _userManager = userManager;
    private readonly RoleManager<IdentityRole> _roleManager = roleManager;
    private readonly ApplicationDbContext _dbContext = dbContext;
    private readonly Microsoft.AspNetCore.Identity.UI.Services.IEmailSender _emailSender = emailSender;
    private readonly Services.AuditService _auditService = auditService;
    private readonly Services.TicketWorkflowService _ticketWorkflowService = ticketWorkflowService;
    private readonly Services.NotificationService _notificationService = notificationService;
    private readonly Services.LookupCacheService _lookupCacheService = lookupCacheService;
    private readonly IConfiguration _configuration = configuration;
    private readonly ILogger<AdminController> _logger = logger;

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

        var admin = await _userManager.GetUserAsync(User);
        await _auditService.Log(admin?.Id, "RoleChange", null, $"Role changed to {role}.", user.Id, user.Email, User.Identity?.Name);

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

        var currentAdmin = await _userManager.GetUserAsync(User);
        if (currentAdmin is not null && currentAdmin.Id == user.Id)
        {
            TempData["Error"] = "You cannot suspend your own account.";
            return RedirectToAction(nameof(Users));
        }

        if (await _userManager.IsInRoleAsync(user, "Admin"))
        {
            var admins = await _userManager.GetUsersInRoleAsync("Admin");
            if (admins.Count <= 1)
            {
                TempData["Error"] = "Cannot suspend the last remaining admin.";
                return RedirectToAction(nameof(Users));
            }
        }

        if (days <= 0)
        {
            TempData["Error"] = "Suspension days must be greater than zero.";
            return RedirectToAction(nameof(Users));
        }

        var until = DateTimeOffset.UtcNow.AddDays(days);
        await _userManager.SetLockoutEndDateAsync(user, until);

        var adminUser = currentAdmin;
        await _auditService.Log(adminUser?.Id, "Suspend", null, reason, user.Id, user.Email, User.Identity?.Name);

        var appealContact = _configuration["Support:AppealContact"] ?? "admin@pup.edu.ph";
        try
        {
            await _emailSender.SendEmailAsync(user.Email!, "Account suspended",
                $"Your account has been suspended until {until:u}. Reason: {reason}. To appeal, contact {appealContact}.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send suspension email.");
        }

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

        var currentAdmin = await _userManager.GetUserAsync(User);
        if (currentAdmin is not null && currentAdmin.Id == user.Id)
        {
            TempData["Error"] = "You cannot deactivate your own account.";
            return RedirectToAction(nameof(Users));
        }

        if (await _userManager.IsInRoleAsync(user, "Admin"))
        {
            var admins = await _userManager.GetUsersInRoleAsync("Admin");
            if (admins.Count <= 1)
            {
                TempData["Error"] = "Cannot deactivate the last remaining admin.";
                return RedirectToAction(nameof(Users));
            }
        }

        user.IsDeactivated = true;
        await _userManager.UpdateAsync(user);
        await _userManager.SetLockoutEndDateAsync(user, DateTimeOffset.MaxValue);

        var adminUser = currentAdmin;
        await _auditService.Log(adminUser?.Id, "Deactivate", null, reason, user.Id, user.Email, User.Identity?.Name);

        var appealContact = _configuration["Support:AppealContact"] ?? "admin@pup.edu.ph";
        try
        {
            await _emailSender.SendEmailAsync(user.Email!, "Account deactivated",
                $"Your account has been deactivated. Reason: {reason}. To appeal, contact {appealContact}.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send deactivation email.");
        }

        return RedirectToAction(nameof(Users));
    }

    public async Task<IActionResult> Tickets(string? status, string? priority, string? category, string? assigneeId, DateTime? from, DateTime? to, string? search, string? sort, int page = 1)
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

        if (string.Equals(sort, "date", StringComparison.OrdinalIgnoreCase))
        {
            query = query.OrderByDescending(t => t.CreatedAt);
        }
        else
        {
            query = query
                .OrderBy(t => t.AssigneeId != null)
                .ThenByDescending(t => t.Priority)
                .ThenByDescending(t => t.CreatedAt);
        }

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
        ViewBag.Sort = sort;

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
        await _auditService.Log(admin?.Id, "Assign", ticket.Id,
            $"Ticket {ticket.TrackingNumber} reassigned from {previousAssigneeId ?? "unassigned"} to {assignee?.Email ?? "unassigned"}.",
            ticket.ReporterId, null, User.Identity?.Name);

        await _ticketWorkflowService.NotifyAssignmentAsync(ticket, assignee, previousAssigneeId, admin?.Id);

        TempData["Message"] = "Ticket assignment updated.";
        return RedirectToAction(nameof(Tickets));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangePriority(int id, TicketPriority priority)
    {
        var admin = await _userManager.GetUserAsync(User);
        var result = await _ticketWorkflowService.ChangePriorityAsync(id, priority, admin?.Id ?? string.Empty, User.IsInRole("Admin"), User.Identity?.Name);
        if (!result.Success)
        {
            TempData["Error"] = result.Error;
        }
        else
        {
            TempData["Message"] = "Priority updated.";
        }
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
        await _auditService.Log(admin?.Id, "MarkInvalid", ticket.Id,
            $"Ticket {ticket.TrackingNumber} marked invalid/spam.", ticket.ReporterId, null, User.Identity?.Name);
        TempData["Message"] = "Ticket marked invalid/spam.";
        return RedirectToAction(nameof(Tickets));
    }

    public async Task<IActionResult> HeatMap(int? buildingId, int? floorId, string? period, DateTime? from, DateTime? to)
    {
        var buildings = _lookupCacheService.GetBuildings();
        var floors = _lookupCacheService.GetFloors();

        int selectedBuildingId = buildingId ?? (buildings.FirstOrDefault()?.Id ?? 0);
        var buildingFloors = floors.Where(f => f.BuildingId == selectedBuildingId).OrderBy(f => f.Name).ToList();
        int selectedFloorId = floorId ?? (buildingFloors.FirstOrDefault()?.Id ?? 0);
        if (buildingFloors.Any() && !buildingFloors.Any(f => f.Id == selectedFloorId))
        {
            selectedFloorId = buildingFloors.First().Id;
        }

        var rooms = _lookupCacheService.GetRooms()
            .Where(r => r.FloorId == selectedFloorId)
            .OrderBy(r => r.Name)
            .ToList();

        var (start, end) = ResolvePeriod(period, from, to);

        // One grouped query per floor.
        var countRows = await _dbContext.Tickets
            .AsNoTracking()
            .Where(t => t.Room != null && t.Room.FloorId == selectedFloorId)
            .Where(t => t.Status != TicketStatus.InvalidSpam)
            .Where(t => t.CreatedAt >= start && t.CreatedAt <= end)
            .GroupBy(t => t.RoomId)
            .Select(g => new { RoomId = g.Key, Count = g.Count() })
            .ToListAsync();

        var counts = rooms.ToDictionary(r => r.Id, r => 0);
        foreach (var row in countRows)
        {
            if (counts.ContainsKey(row.RoomId))
            {
                counts[row.RoomId] = row.Count;
            }
        }

        var roomData = rooms.Select(r => new
        {
            id = r.Id,
            name = r.Name,
            count = counts[r.Id],
            historyUrl = Url.Action("RoomHistory", "Admin", new { id = r.Id })
        }).ToList();

        ViewBag.Buildings = buildings;
        ViewBag.Floors = floors;
        ViewBag.Rooms = rooms;
        ViewBag.SelectedBuildingId = selectedBuildingId;
        ViewBag.SelectedFloorId = selectedFloorId;
        ViewBag.Period = period ?? "30";
        ViewBag.From = start;
        ViewBag.To = end;
        ViewBag.Counts = counts;
        ViewBag.RoomDataJson = System.Text.Json.JsonSerializer.Serialize(roomData);

        return View();
    }

    [HttpGet]
    public async Task<IActionResult> HeatMapCounts(int floorId, string? period, DateTime? from, DateTime? to)
    {
        var (start, end) = ResolvePeriod(period, from, to);

        var counts = await _dbContext.Tickets
            .AsNoTracking()
            .Where(t => t.Room != null && t.Room.FloorId == floorId)
            .Where(t => t.Status != TicketStatus.InvalidSpam)
            .Where(t => t.CreatedAt >= start && t.CreatedAt <= end)
            .GroupBy(t => t.RoomId)
            .Select(g => new { roomId = g.Key, count = g.Count() })
            .ToListAsync();

        return Json(counts);
    }

    private static (DateTime Start, DateTime End) ResolvePeriod(string? period, DateTime? from, DateTime? to)
    {
        var now = DateTime.UtcNow;

        if (string.Equals(period, "custom", StringComparison.OrdinalIgnoreCase) && from.HasValue && to.HasValue)
        {
            var start = from.Value.Date;
            var end = to.Value.Date.AddDays(1).AddTicks(-1);
            if (end < start)
            {
                end = start;
            }
            return (start, end);
        }

        if (string.Equals(period, "semester", StringComparison.OrdinalIgnoreCase))
        {
            int year = now.Year;
            if (now.Month >= 8)
            {
                return (new DateTime(year, 8, 1), new DateTime(year, 12, 31, 23, 59, 59));
            }
            if (now.Month >= 6)
            {
                return (new DateTime(year, 6, 1), new DateTime(year, 7, 31, 23, 59, 59));
            }
            return (new DateTime(year, 1, 1), new DateTime(year, 5, 31, 23, 59, 59));
        }

        // Default: last 30 days.
        return (now.AddDays(-30), now);
    }

    public async Task<IActionResult> Resolution(DateTime? from, DateTime? to)
    {
        var start = (from ?? DateTime.UtcNow.AddDays(-30)).Date;
        var end = (to ?? DateTime.UtcNow).Date.AddDays(1).AddTicks(-1);

        var baseQuery = _dbContext.Tickets.AsNoTracking()
            .Where(t => t.CreatedAt >= start && t.CreatedAt <= end && t.Status != TicketStatus.InvalidSpam);

        // Overall averages.
        var completionCount = await baseQuery.CountAsync(t => t.VerificationStartedAt != null);
        double? completionAvg = null;
        if (completionCount > 0)
        {
            completionAvg = await _dbContext.Database.SqlQueryRaw<double?>(
                "SELECT AVG((julianday(VerificationStartedAt) - julianday(CreatedAt)) * 24.0) AS Value FROM Tickets WHERE CreatedAt >= @s AND CreatedAt <= @e AND Status <> @invalid AND VerificationStartedAt IS NOT NULL",
                new Microsoft.Data.Sqlite.SqliteParameter("@s", start),
                new Microsoft.Data.Sqlite.SqliteParameter("@e", end),
                new Microsoft.Data.Sqlite.SqliteParameter("@invalid", (int)TicketStatus.InvalidSpam))
                .FirstOrDefaultAsync();
        }

        var closedCount = await baseQuery.CountAsync(t => t.ClosedAt != null);
        double? closedAvg = null;
        if (closedCount > 0)
        {
            closedAvg = await _dbContext.Database.SqlQueryRaw<double?>(
                "SELECT AVG((julianday(ClosedAt) - julianday(CreatedAt)) * 24.0) AS Value FROM Tickets WHERE CreatedAt >= @s AND CreatedAt <= @e AND Status <> @invalid AND ClosedAt IS NOT NULL",
                new Microsoft.Data.Sqlite.SqliteParameter("@s", start),
                new Microsoft.Data.Sqlite.SqliteParameter("@e", end),
                new Microsoft.Data.Sqlite.SqliteParameter("@invalid", (int)TicketStatus.InvalidSpam))
                .FirstOrDefaultAsync();
        }

        // Per category.
        var categoryRows = new List<object>();
        var categoryLabels = new List<string>();
        var categoryCompletionAvgs = new List<double?>();
        foreach (var category in Enum.GetValues<TicketCategory>())
        {
            var catQuery = baseQuery.Where(t => t.Category == category);
            var catCompletionCount = await catQuery.CountAsync(t => t.VerificationStartedAt != null);
            double? catCompletionAvg = null;
            if (catCompletionCount > 0)
            {
                catCompletionAvg = await _dbContext.Database.SqlQueryRaw<double?>(
                    "SELECT AVG((julianday(VerificationStartedAt) - julianday(CreatedAt)) * 24.0) AS Value FROM Tickets WHERE CreatedAt >= @s AND CreatedAt <= @e AND Status <> @invalid AND Category = @cat AND VerificationStartedAt IS NOT NULL",
                    new Microsoft.Data.Sqlite.SqliteParameter("@s", start),
                    new Microsoft.Data.Sqlite.SqliteParameter("@e", end),
                    new Microsoft.Data.Sqlite.SqliteParameter("@invalid", (int)TicketStatus.InvalidSpam),
                    new Microsoft.Data.Sqlite.SqliteParameter("@cat", (int)category))
                    .FirstOrDefaultAsync();
            }
            var catClosedCount = await catQuery.CountAsync(t => t.ClosedAt != null);
            double? catClosedAvg = null;
            if (catClosedCount > 0)
            {
                catClosedAvg = await _dbContext.Database.SqlQueryRaw<double?>(
                    "SELECT AVG((julianday(ClosedAt) - julianday(CreatedAt)) * 24.0) AS Value FROM Tickets WHERE CreatedAt >= @s AND CreatedAt <= @e AND Status <> @invalid AND Category = @cat AND ClosedAt IS NOT NULL",
                    new Microsoft.Data.Sqlite.SqliteParameter("@s", start),
                    new Microsoft.Data.Sqlite.SqliteParameter("@e", end),
                    new Microsoft.Data.Sqlite.SqliteParameter("@invalid", (int)TicketStatus.InvalidSpam),
                    new Microsoft.Data.Sqlite.SqliteParameter("@cat", (int)category))
                    .FirstOrDefaultAsync();
            }
            var catTotal = await catQuery.CountAsync();
            categoryRows.Add(new
            {
                Category = category.ToString(),
                Total = catTotal,
                CompletionCount = catCompletionCount,
                CompletionAvg = catCompletionAvg,
                ClosedCount = catClosedCount,
                ClosedAvg = catClosedAvg
            });
            categoryLabels.Add(category.ToString());
            categoryCompletionAvgs.Add(catCompletionAvg ?? 0);
        }

        // Staff workload: include IT and maintenance staff with zero tickets.
        var staff = new List<ApplicationUser>();
        foreach (var role in new[] { "ITPersonnel", "MaintenancePersonnel" })
        {
            staff.AddRange(await _userManager.GetUsersInRoleAsync(role));
        }
        staff = staff.DistinctBy(u => u.Id).OrderBy(u => u.Email).ToList();

        var openCounts = await baseQuery
            .Where(t => t.Status != TicketStatus.Closed)
            .GroupBy(t => t.AssigneeId)
            .Select(g => new { AssigneeId = g.Key, Count = g.Count() })
            .ToListAsync();
        var closedCounts = await _dbContext.Tickets.AsNoTracking()
            .Where(t => t.CreatedAt >= start && t.CreatedAt <= end && t.Status == TicketStatus.Closed)
            .GroupBy(t => t.AssigneeId)
            .Select(g => new { AssigneeId = g.Key, Count = g.Count() })
            .ToListAsync();

        var staffRoles = new Dictionary<string, string?>();
        foreach (var s in staff)
        {
            var roles = await _userManager.GetRolesAsync(s);
            staffRoles[s.Id] = roles.FirstOrDefault();
        }

        var workloadRows = staff.Select(s => new
        {
            Email = s.Email,
            Role = staffRoles[s.Id],
            Open = openCounts.FirstOrDefault(x => x.AssigneeId == s.Id)?.Count ?? 0,
            Closed = closedCounts.FirstOrDefault(x => x.AssigneeId == s.Id)?.Count ?? 0
        }).ToList();

        // Source comparison.
        var sourceRows = await baseQuery
            .GroupBy(t => t.Source)
            .Select(g => new { Source = g.Key, Count = g.Count() })
            .ToListAsync();
        var totalSource = sourceRows.Sum(x => x.Count);
        var sourceLabels = sourceRows.Select(x => x.Source).ToList();
        var sourceCounts = sourceRows.Select(x => x.Count).ToList();
        var sourcePercents = sourceRows.Select(x => totalSource > 0 ? (double)x.Count / totalSource * 100.0 : 0.0).ToList();

        ViewBag.From = start;
        ViewBag.To = end;
        ViewBag.CompletionCount = completionCount;
        ViewBag.CompletionAvg = completionAvg;
        ViewBag.ClosedCount = closedCount;
        ViewBag.ClosedAvg = closedAvg;
        ViewBag.CategoryRows = categoryRows;
        ViewBag.CategoryLabels = categoryLabels;
        ViewBag.CategoryCompletionAvgs = categoryCompletionAvgs;
        ViewBag.WorkloadRows = workloadRows;
        ViewBag.SourceLabels = sourceLabels;
        ViewBag.SourceCounts = sourceCounts;
        ViewBag.SourcePercents = sourcePercents;
        ViewBag.TotalSource = totalSource;

        return View();
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

    public async Task<IActionResult> AuditLogs(string? userId, DateTime? from, DateTime? to, int page = 1)
    {
        const int pageSize = 20;

        var action = Request.Query["action"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(action))
        {
            action = Request.Query["actionType"].FirstOrDefault();
        }

        var query = _dbContext.AuditLogs.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(userId))
        {
            query = query.Where(a => a.ActorId == userId || a.AdminId == userId || a.TargetUserId == userId);
        }
        if (!string.IsNullOrWhiteSpace(action))
        {
            query = query.Where(a => a.Action == action);
        }
        if (from.HasValue)
        {
            query = query.Where(a => a.CreatedAt >= from.Value);
        }
        if (to.HasValue)
        {
            query = query.Where(a => a.CreatedAt <= to.Value.AddDays(1));
        }

        query = query.OrderByDescending(a => a.CreatedAt);

        var total = await query.CountAsync();
        var logs = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

        ViewBag.Total = total;
        ViewBag.Page = page;
        ViewBag.PageSize = pageSize;
        ViewBag.TotalPages = (int)Math.Ceiling(total / (double)pageSize);
        ViewBag.UserId = userId;
        ViewBag.Action = action;
        ViewBag.From = from;
        ViewBag.To = to;
        ViewBag.Users = _userManager.Users.OrderBy(u => u.Email).ToList();
        ViewBag.Actions = await _dbContext.AuditLogs.Select(a => a.Action).Distinct().OrderBy(a => a).ToListAsync();

        return View(logs);
    }

    public async Task<IActionResult> RoomHistory(int id)
    {
        var room = await _dbContext.Rooms
            .Include(r => r.Floor).ThenInclude(f => f!.Building)
            .FirstOrDefaultAsync(r => r.Id == id);
        if (room is null) return NotFound();

        var tickets = await _dbContext.Tickets
            .AsNoTracking()
            .Include(t => t.Equipment)
            .Include(t => t.Reporter)
            .Include(t => t.Assignee)
            .Where(t => t.RoomId == id)
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync();

        ViewBag.Tickets = tickets;
        return View(room);
    }

    public IActionResult Locations()
    {
        ViewBag.Buildings = _lookupCacheService.GetBuildings();
        ViewBag.Floors = _lookupCacheService.GetFloors();
        ViewBag.Rooms = _lookupCacheService.GetRooms();
        ViewBag.Equipment = _lookupCacheService.GetEquipment();
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
            _lookupCacheService.Invalidate();

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
    public async Task<IActionResult> CreateEquipment(string name, int roomId, bool isSafetyHazard)
    {
        if (!string.IsNullOrWhiteSpace(name) && roomId > 0)
        {
            _dbContext.Equipment.Add(new Equipment { Name = name.Trim(), RoomId = roomId, IsSafetyHazard = isSafetyHazard });
            await _dbContext.SaveChangesAsync();
        }
        return RedirectToAction(nameof(Locations));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditEquipment(int id, string name, int roomId, bool isSafetyHazard)
    {
        var eq = await _dbContext.Equipment.FindAsync(id);
        if (eq is not null && !string.IsNullOrWhiteSpace(name) && roomId > 0)
        {
            eq.Name = name.Trim();
            eq.RoomId = roomId;
            eq.IsSafetyHazard = isSafetyHazard;
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

        var adminUser = await _userManager.GetUserAsync(User);
        await _auditService.Log(adminUser?.Id, "Reactivate", null, reason, user.Id, user.Email, User.Identity?.Name);

        var appealContact = _configuration["Support:AppealContact"] ?? "admin@pup.edu.ph";
        try
        {
            await _emailSender.SendEmailAsync(user.Email!, "Account reactivated",
                $"Your account has been reactivated. Reason: {reason}. To appeal, contact {appealContact}.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send reactivation email.");
        }

        // In-app notification since the user can log in again.
        try
        {
            await _notificationService.NotifyAsync(user.Id, null, "Account",
                $"Your account has been reactivated. Reason: {reason}.", null, false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create reactivation notification.");
        }

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
