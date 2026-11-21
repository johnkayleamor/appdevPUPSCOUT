using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using appdevPUPSCOUT.Data;
using appdevPUPSCOUT.Models;

namespace appdevPUPSCOUT.Controllers;

[Authorize]
public class TicketsController(
    ApplicationDbContext dbContext,
    UserManager<ApplicationUser> userManager,
    IWebHostEnvironment environment,
    Services.TicketWorkflowService ticketWorkflowService,
    Microsoft.AspNetCore.Identity.UI.Services.IEmailSender emailSender,
    Services.AuditService auditService) : Controller
{
    private readonly ApplicationDbContext _dbContext = dbContext;
    private readonly UserManager<ApplicationUser> _userManager = userManager;
    private readonly IWebHostEnvironment _environment = environment;
    private readonly Services.TicketWorkflowService _ticketWorkflowService = ticketWorkflowService;
    private readonly Microsoft.AspNetCore.Identity.UI.Services.IEmailSender _emailSender = emailSender;
    private readonly Services.AuditService _auditService = auditService;

    public IActionResult Create(int? buildingId, int? floorId, int? roomId)
    {
        var model = new TicketCreateViewModel
        {
            BuildingId = buildingId ?? 0,
            FloorId = floorId ?? 0,
            RoomId = roomId ?? 0
        };
        ViewBag.Buildings = _dbContext.Buildings.OrderBy(b => b.Name).ToList();
        ViewBag.Floors = _dbContext.Floors.Include(f => f.Building).OrderBy(f => f.Name).ToList();
        ViewBag.Rooms = _dbContext.Rooms.Include(r => r.Floor).ThenInclude(f => f!.Building).Where(r => r.IsActive).OrderBy(r => r.Name).ToList();
        ViewBag.Equipment = _dbContext.Equipment.Include(e => e.Room).ThenInclude(r => r!.Floor).ThenInclude(f => f!.Building).Where(e => e.Room != null && e.Room!.IsActive).OrderBy(e => e.Name).ToList();
        return View(model);
    }

    [HttpGet]
    public IActionResult GetFloors(int buildingId)
    {
        var floors = _dbContext.Floors
            .Where(f => f.BuildingId == buildingId)
            .OrderBy(f => f.Name)
            .Select(f => new { id = f.Id, name = f.Name })
            .ToList();
        return Json(floors);
    }

    [HttpGet]
    public IActionResult FloorsByBuilding(int buildingId) => GetFloors(buildingId);

    [HttpGet]
    public IActionResult GetFloorsByBuilding(int buildingId) => GetFloors(buildingId);

    [HttpGet]
    public IActionResult GetRooms(int floorId)
    {
        var rooms = _dbContext.Rooms
            .Where(r => r.FloorId == floorId)
            .OrderBy(r => r.Name)
            .Select(r => new { id = r.Id, name = r.Name, isActive = r.IsActive, status = r.IsActive ? "Active" : "Inactive" })
            .ToList();
        return Json(rooms);
    }

    [HttpGet]
    public IActionResult RoomsByFloor(int floorId) => GetRooms(floorId);

    [HttpGet]
    public IActionResult GetRoomsByFloor(int floorId) => GetRooms(floorId);

    [HttpGet]
    public IActionResult GetOpenTickets(int roomId, int equipmentId)
    {
        var openStatuses = new[] { TicketStatus.Pending, TicketStatus.Assigned, TicketStatus.InProgress, TicketStatus.PendingVerification };
        var tickets = _dbContext.Tickets
            .AsNoTracking()
            .Where(t => t.RoomId == roomId && t.EquipmentId == equipmentId && openStatuses.Contains(t.Status))
            .OrderByDescending(t => t.CreatedAt)
            .Select(t => new { id = t.Id, trackingNumber = t.TrackingNumber, status = t.Status.ToString() })
            .ToList();
        return Json(tickets);
    }

    [HttpGet]
    public IActionResult GetEquipment(int roomId)
    {
        var equipment = _dbContext.Equipment
            .Where(e => e.RoomId == roomId)
            .OrderBy(e => e.Name)
            .Select(e => new { id = e.Id, name = e.Name })
            .ToList();
        return Json(equipment);
    }

    [HttpGet]
    public IActionResult EquipmentByRoom(int roomId) => GetEquipment(roomId);

    public async Task<IActionResult> Map(int? buildingId, int? floorId)
    {
        var buildings = _dbContext.Buildings.OrderBy(b => b.Name).ToList();
        if (!buildings.Any())
        {
            ViewBag.Buildings = buildings;
            ViewBag.Floors = new List<Floor>();
            ViewBag.Rooms = new List<Room>();
            ViewBag.RoomStatuses = new Dictionary<int, string>();
            ViewBag.EquipmentByRoom = new Dictionary<int, List<Equipment>>();
            ViewBag.OpenEquipmentIds = new HashSet<int>();
            ViewBag.SelectedBuildingId = 0;
            ViewBag.SelectedFloorId = 0;
            ViewBag.RoomDataJson = "[]";
            return View();
        }

        var selectedBuildingId = buildingId ?? buildings.First().Id;
        var floors = _dbContext.Floors.Where(f => f.BuildingId == selectedBuildingId).OrderBy(f => f.Name).ToList();
        if (!floors.Any())
        {
            ViewBag.Buildings = buildings;
            ViewBag.Floors = _dbContext.Floors.Include(f => f.Building).OrderBy(f => f.Name).ToList();
            ViewBag.SelectedBuildingId = selectedBuildingId;
            ViewBag.SelectedFloorId = 0;
            ViewBag.Rooms = new List<Room>();
            ViewBag.RoomStatuses = new Dictionary<int, string>();
            ViewBag.EquipmentByRoom = new Dictionary<int, List<Equipment>>();
            ViewBag.OpenEquipmentIds = new HashSet<int>();
            ViewBag.RoomDataJson = "[]";
            return View();
        }

        var selectedFloorId = floorId ?? floors.First().Id;
        if (!floors.Any(f => f.Id == selectedFloorId))
        {
            selectedFloorId = floors.First().Id;
        }

        var rooms = _dbContext.Rooms
            .Where(r => r.FloorId == selectedFloorId)
            .OrderBy(r => r.Name)
            .ToList();

        var openStatuses = new[] { TicketStatus.Pending, TicketStatus.Assigned, TicketStatus.InProgress, TicketStatus.PendingVerification };

        var openTickets = await _dbContext.Tickets
            .AsNoTracking()
            .Include(t => t.Room)
            .Include(t => t.Equipment)
            .Where(t => t.Room != null && t.Room.FloorId == selectedFloorId && openStatuses.Contains(t.Status))
            .ToListAsync();

        var statusByRoom = new Dictionary<int, string>();
        foreach (var room in rooms)
        {
            var roomTickets = openTickets.Where(t => t.RoomId == room.Id).ToList();
            if (!roomTickets.Any())
            {
                statusByRoom[room.Id] = "No open reports";
            }
            else if (roomTickets.Any(t => t.Status == TicketStatus.Pending))
            {
                statusByRoom[room.Id] = "Unresolved";
            }
            else
            {
                statusByRoom[room.Id] = "In progress";
            }
        }

        var equipmentByRoom = rooms.ToDictionary(r => r.Id, r => _dbContext.Equipment.Where(e => e.RoomId == r.Id).OrderBy(e => e.Name).ToList());
        var openEquipmentIds = new HashSet<int>(openTickets.Select(t => t.EquipmentId));

        var roomData = rooms.Select(r => new
        {
            id = r.Id,
            name = r.Name,
            status = statusByRoom[r.Id],
            hasOpenTicket = openTickets.Any(t => t.RoomId == r.Id),
            equipment = equipmentByRoom[r.Id].Select(e => new
            {
                name = e.Name,
                hasOpen = openEquipmentIds.Contains(e.Id)
            }).ToList()
        }).ToList();

        ViewBag.Buildings = buildings;
        ViewBag.Floors = _dbContext.Floors.Include(f => f.Building).OrderBy(f => f.Name).ToList();
        ViewBag.SelectedBuildingId = selectedBuildingId;
        ViewBag.SelectedFloorId = selectedFloorId;
        ViewBag.Rooms = rooms;
        ViewBag.RoomStatuses = statusByRoom;
        ViewBag.EquipmentByRoom = equipmentByRoom;
        ViewBag.OpenEquipmentIds = openEquipmentIds;
        ViewBag.RoomDataJson = System.Text.Json.JsonSerializer.Serialize(roomData);

        return View();
    }

    public async Task<IActionResult> Details(int id)
    {
        var ticket = await _dbContext.Tickets
            .AsNoTracking()
            .Include(t => t.Room).ThenInclude(r => r!.Floor).ThenInclude(f => f!.Building)
            .Include(t => t.Equipment)
            .Include(t => t.Photos)
            .Include(t => t.Notes).ThenInclude(n => n.Author)
            .Include(t => t.Comments).ThenInclude(c => c.Author)
            .Include(t => t.StatusHistory).ThenInclude(h => h.ChangedBy)
            .FirstOrDefaultAsync(t => t.Id == id);

        if (ticket is null) return NotFound();

        var user = await _userManager.GetUserAsync(User);
        if (user is null) return Challenge();

        if (ticket.ReporterId != user.Id && ticket.AssigneeId != user.Id && !User.IsInRole("Admin"))
        {
            return Forbid();
        }

        return View(ticket);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateStatus(int id, TicketStatus newStatus, string? note)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null) return Challenge();

        var ticket = await _dbContext.Tickets.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id);
        if (ticket is null) return NotFound();

        if (ticket.AssigneeId != user.Id && !User.IsInRole("Admin"))
        {
            return Forbid();
        }

        var result = await _ticketWorkflowService.ChangeStatusAsync(id, newStatus, user.Id, note);
        if (!result.Success)
        {
            TempData["Error"] = result.Error;
        }
        else
        {
            TempData["Message"] = "Status updated.";
        }

        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ConfirmFix(int id)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null) return Challenge();

        var ticket = await _dbContext.Tickets
            .Include(t => t.Reporter)
            .FirstOrDefaultAsync(t => t.Id == id);
        if (ticket is null) return NotFound();

        var isAdmin = User.IsInRole("Admin");
        if (!CanConfirmOrReject(ticket, user, isAdmin))
        {
            return Forbid();
        }

        if (ticket.Status != TicketStatus.PendingVerification)
        {
            TempData["Error"] = "Ticket is not pending verification.";
            return RedirectToAction(nameof(Details), new { id });
        }

        string? reason = null;
        if (isAdmin)
        {
            reason = Request.Form["reason"].ToString();
            if (string.IsNullOrWhiteSpace(reason))
            {
                TempData["Error"] = "A reason is required.";
                return RedirectToAction(nameof(Details), new { id });
            }
        }

        var result = await _ticketWorkflowService.ConfirmAsync(id, user.Id, isAdmin, reason);
        if (!result.Success)
        {
            TempData["Error"] = result.Error;
        }
        else
        {
            TempData["Message"] = isAdmin ? "Ticket closed by admin." : "Ticket confirmed and closed.";
        }
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RejectFix(int id, string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            TempData["Error"] = "A reason is required to reject the fix.";
            return RedirectToAction(nameof(Details), new { id });
        }

        var user = await _userManager.GetUserAsync(User);
        if (user is null) return Challenge();

        var ticket = await _dbContext.Tickets
            .Include(t => t.Reporter)
            .FirstOrDefaultAsync(t => t.Id == id);
        if (ticket is null) return NotFound();

        var isAdmin = User.IsInRole("Admin");
        if (!CanConfirmOrReject(ticket, user, isAdmin))
        {
            return Forbid();
        }

        if (ticket.Status != TicketStatus.PendingVerification)
        {
            TempData["Error"] = "Ticket is not pending verification.";
            return RedirectToAction(nameof(Details), new { id });
        }

        var result = await _ticketWorkflowService.RejectAsync(id, user.Id, reason!, isAdmin);
        if (!result.Success)
        {
            TempData["Error"] = result.Error;
        }
        else
        {
            TempData["Message"] = "Fix rejected; ticket returned to In Progress.";
        }
        return RedirectToAction(nameof(Details), new { id });
    }

    private bool CanConfirmOrReject(Ticket ticket, ApplicationUser user, bool isAdmin)
    {
        if (isAdmin) return true;
        if (ticket.ReporterId == user.Id && ticket.ReporterId != ticket.AssigneeId) return true;
        return false;
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddComment(int id, string? content)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null) return Challenge();

        var ticket = await _dbContext.Tickets.FirstOrDefaultAsync(t => t.Id == id);
        if (ticket is null) return NotFound();

        if (!CanAccessTicket(ticket, user))
        {
            return Forbid();
        }

        if (ticket.Status == TicketStatus.Closed || ticket.Status == TicketStatus.InvalidSpam)
        {
            TempData["Error"] = "Comments are read-only for this ticket.";
            return RedirectToAction(nameof(Details), new { id });
        }

        if (string.IsNullOrWhiteSpace(content) || content.Trim().Length > 2000)
        {
            TempData["Error"] = "A comment is required and must be 2000 characters or fewer.";
            return RedirectToAction(nameof(Details), new { id });
        }

        var roles = await _userManager.GetRolesAsync(user);
        _dbContext.TicketComments.Add(new TicketComment
        {
            TicketId = ticket.Id,
            AuthorId = user.Id,
            AuthorRole = roles.FirstOrDefault() ?? "User",
            Content = content.Trim(),
            CreatedAt = DateTime.UtcNow
        });

        await _dbContext.SaveChangesAsync();

        var recipients = new HashSet<string>();
        if (ticket.ReporterId != user.Id) recipients.Add(ticket.ReporterId);
        if (!string.IsNullOrWhiteSpace(ticket.AssigneeId) && ticket.AssigneeId != user.Id) recipients.Add(ticket.AssigneeId!);

        foreach (var recipientId in recipients)
        {
            var recipient = await _userManager.FindByIdAsync(recipientId);
            if (recipient?.Email is not null)
            {
                await _emailSender.SendEmailAsync(recipient.Email,
                    $"New comment on ticket {ticket.TrackingNumber}",
                    $"A new comment was added to ticket {ticket.TrackingNumber}.");
            }
        }

        TempData["Message"] = "Comment added.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveComment(int id, int commentId)
    {
        if (!User.IsInRole("Admin")) return Forbid();

        var user = await _userManager.GetUserAsync(User);
        if (user is null) return Challenge();

        var ticket = await _dbContext.Tickets.FirstOrDefaultAsync(t => t.Id == id);
        if (ticket is null) return NotFound();

        var comment = await _dbContext.TicketComments.FirstOrDefaultAsync(c => c.Id == commentId && c.TicketId == id);
        if (comment is null) return NotFound();

        _dbContext.TicketComments.Remove(comment);
        await _dbContext.SaveChangesAsync();

        await _auditService.Log(user.Id, "RemoveComment", ticket.Id,
            $"Comment {commentId} removed.", ticket.ReporterId, null, user.Email);

        TempData["Message"] = "Comment removed.";
        return RedirectToAction(nameof(Details), new { id });
    }

    private bool CanAccessTicket(Ticket ticket, ApplicationUser user)
    {
        if (User.IsInRole("Admin")) return true;
        if (ticket.ReporterId == user.Id) return true;
        if (!string.IsNullOrWhiteSpace(ticket.AssigneeId) && ticket.AssigneeId == user.Id) return true;
        return false;
    }

    public async Task<IActionResult> Assigned(string? status, string? priority)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null) return Challenge();

        var query = _dbContext.Tickets
            .AsNoTracking()
            .Include(t => t.Room).ThenInclude(r => r!.Floor).ThenInclude(f => f!.Building)
            .Include(t => t.Equipment)
            .Include(t => t.Reporter)
            .Where(t => t.AssigneeId == user.Id)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<TicketStatus>(status, true, out var st))
        {
            query = query.Where(t => t.Status == st);
        }
        if (!string.IsNullOrWhiteSpace(priority) && Enum.TryParse<TicketPriority>(priority, true, out var pr))
        {
            query = query.Where(t => t.Priority == pr);
        }

        var tickets = await query
            .OrderByDescending(t => t.Priority)
            .ThenByDescending(t => t.CreatedAt)
            .ToListAsync();

        ViewBag.Status = status;
        ViewBag.Priority = priority;
        return View(tickets);
    }

    public async Task<IActionResult> Track(string? trackingNumber)
    {
        if (string.IsNullOrWhiteSpace(trackingNumber))
        {
            return RedirectToAction(nameof(MyReports));
        }

        var ticket = await _dbContext.Tickets.FirstOrDefaultAsync(t => t.TrackingNumber == trackingNumber.ToUpperInvariant());
        if (ticket is null)
        {
            TempData["Error"] = "Ticket not found.";
            return RedirectToAction(nameof(MyReports));
        }

        var user = await _userManager.GetUserAsync(User);
        if (user is null) return Challenge();

        if (ticket.ReporterId != user.Id && !User.IsInRole("Admin"))
        {
            return Forbid();
        }

        return RedirectToAction(nameof(Details), new { id = ticket.Id });
    }

    public async Task<IActionResult> MyReports(string? filter)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null) return Challenge();

        var openStatuses = new[] { TicketStatus.Pending, TicketStatus.Assigned, TicketStatus.InProgress, TicketStatus.PendingVerification };

        var query = _dbContext.Tickets
            .AsNoTracking()
            .Include(t => t.Room).ThenInclude(r => r!.Floor).ThenInclude(f => f!.Building)
            .Include(t => t.Equipment)
            .Where(t => t.ReporterId == user.Id);

        if (string.Equals(filter, "open", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(t => openStatuses.Contains(t.Status));
        }
        else if (string.Equals(filter, "closed", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(t => t.Status == TicketStatus.Closed || t.Status == TicketStatus.InvalidSpam);
        }

        var tickets = await query.OrderByDescending(t => t.CreatedAt).ToListAsync();

        ViewBag.Filter = string.IsNullOrWhiteSpace(filter) ? "all" : filter.ToLowerInvariant();
        return View(tickets);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("ReportSubmission")]
    public async Task<IActionResult> Create(TicketCreateViewModel model)
    {
        var uploadedPhotos = new List<IFormFile>();
        if (model.Image is not null) uploadedPhotos.Add(model.Image);
        if (model.Photos is not null) uploadedPhotos.AddRange(model.Photos);

        foreach (var photo in uploadedPhotos)
        {
            if (!photo.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            {
                ModelState.AddModelError(nameof(model.Photos), "Only image files are allowed.");
            }

            if (photo.Length > 5 * 1024 * 1024)
            {
                ModelState.AddModelError(nameof(model.Photos), "Each image must be 5 MB or smaller.");
            }
        }

        if (!ModelState.IsValid)
        {
            ViewBag.Buildings = _dbContext.Buildings.OrderBy(b => b.Name).ToList();
            ViewBag.Floors = _dbContext.Floors.Include(f => f.Building).OrderBy(f => f.Name).ToList();
            ViewBag.Rooms = _dbContext.Rooms.Include(r => r.Floor).ThenInclude(f => f!.Building).Where(r => r.IsActive).OrderBy(r => r.Name).ToList();
            ViewBag.Equipment = _dbContext.Equipment.Include(e => e.Room).ThenInclude(r => r!.Floor).ThenInclude(f => f!.Building).Where(e => e.Room != null && e.Room!.IsActive).OrderBy(e => e.Name).ToList();
            return View(model);
        }

        var user = await _userManager.GetUserAsync(User);
        if (user is null)
        {
            return Challenge();
        }

        var ticket = await _ticketWorkflowService.CreateAsync(model, user);

        if (uploadedPhotos.Count > 0)
        {
            var uploads = Path.Combine(_environment.WebRootPath, "uploads", "tickets", ticket.Id.ToString());
            Directory.CreateDirectory(uploads);

            foreach (var photo in uploadedPhotos)
            {
                var fileName = Guid.NewGuid().ToString("N") + Path.GetExtension(photo.FileName);
                var filePath = Path.Combine(uploads, fileName);
                await using var stream = System.IO.File.Create(filePath);
                await photo.CopyToAsync(stream);

                var relativePath = Path.Combine("uploads", "tickets", ticket.Id.ToString(), fileName).Replace("\\", "/");
                _dbContext.TicketPhotos.Add(new TicketPhoto
                {
                    TicketId = ticket.Id,
                    FilePath = relativePath
                });
            }

            await _dbContext.SaveChangesAsync();
        }

        TempData["Message"] = $"Report submitted. Tracking: {ticket.TrackingNumber}";
        return RedirectToAction("Index", "Home");
    }
}
