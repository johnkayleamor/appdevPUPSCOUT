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
    Services.AuditService auditService,
    Services.NotificationService notificationService,
    Services.LookupCacheService lookupCacheService) : Controller
{
    private readonly ApplicationDbContext _dbContext = dbContext;
    private readonly UserManager<ApplicationUser> _userManager = userManager;
    private readonly IWebHostEnvironment _environment = environment;
    private readonly Services.TicketWorkflowService _ticketWorkflowService = ticketWorkflowService;
    private readonly Microsoft.AspNetCore.Identity.UI.Services.IEmailSender _emailSender = emailSender;
    private readonly Services.AuditService _auditService = auditService;
    private readonly Services.NotificationService _notificationService = notificationService;
    private readonly Services.LookupCacheService _lookupCacheService = lookupCacheService;

    public IActionResult Create(int? buildingId, int? floorId, int? roomId, string? source)
    {
        var model = new TicketCreateViewModel
        {
            BuildingId = buildingId ?? 0,
            FloorId = floorId ?? 0,
            RoomId = roomId ?? 0,
            Source = NormalizeSource(source)
        };
        ViewBag.Buildings = _lookupCacheService.GetBuildings();
        ViewBag.Floors = _lookupCacheService.GetFloors();
        ViewBag.Rooms = _lookupCacheService.GetRooms().Where(r => r.IsActive).OrderBy(r => r.Name).ToList();
        ViewBag.Equipment = _lookupCacheService.GetEquipment().Where(e => e.Room != null && e.Room!.IsActive).OrderBy(e => e.Name).ToList();
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
        var buildings = _lookupCacheService.GetBuildings();
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
        var floors = _lookupCacheService.GetFloors().Where(f => f.BuildingId == selectedBuildingId).OrderBy(f => f.Name).ToList();
        if (!floors.Any())
        {
            ViewBag.Buildings = buildings;
            ViewBag.Floors = _lookupCacheService.GetFloors();
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

        var rooms = _lookupCacheService.GetRooms()
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

        var equipmentByRoom = rooms.ToDictionary(r => r.Id, r => _lookupCacheService.GetEquipment().Where(e => e.RoomId == r.Id).OrderBy(e => e.Name).ToList());
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
        ViewBag.Floors = _lookupCacheService.GetFloors();
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

        var result = await _ticketWorkflowService.AddCommentAsync(id, user.Id, content ?? string.Empty);
        if (!result.Success)
        {
            TempData["Error"] = result.Error;
        }
        else
        {
            TempData["Message"] = "Comment added.";
        }

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

    private static string NormalizeSource(string? source)
    {
        return string.Equals(source, "Map", StringComparison.OrdinalIgnoreCase) ? "Map" : "List";
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
    [RequestSizeLimit(30 * 1024 * 1024)]
    public async Task<IActionResult> Create(TicketCreateViewModel model)
    {
        var uploadedPhotos = new List<IFormFile>();
        if (model.Image is not null) uploadedPhotos.Add(model.Image);
        if (model.Photos is not null) uploadedPhotos.AddRange(model.Photos);

        const int maxPhotosPerTicket = 5;
        if (uploadedPhotos.Count > maxPhotosPerTicket)
        {
            ModelState.AddModelError(nameof(model.Photos), $"You can upload at most {maxPhotosPerTicket} photos per ticket.");
        }

        foreach (var photo in uploadedPhotos)
        {
            if (photo.Length == 0)
            {
                ModelState.AddModelError(nameof(model.Photos), "Empty files are not allowed.");
                continue;
            }

            if (photo.Length > 5 * 1024 * 1024)
            {
                ModelState.AddModelError(nameof(model.Photos), "Each image must be 5 MB or smaller.");
                continue;
            }

            // Validate by magic bytes, never by client Content-Type or file name.
            var detectedExtension = DetectImageExtension(photo);
            if (detectedExtension is null)
            {
                ModelState.AddModelError(nameof(model.Photos), "Only JPEG and PNG images are allowed.");
            }
        }

        if (!ModelState.IsValid)
        {
            ViewBag.Buildings = _lookupCacheService.GetBuildings();
            ViewBag.Floors = _lookupCacheService.GetFloors();
            ViewBag.Rooms = _lookupCacheService.GetRooms().Where(r => r.IsActive).OrderBy(r => r.Name).ToList();
            ViewBag.Equipment = _lookupCacheService.GetEquipment().Where(e => e.Room != null && e.Room!.IsActive).OrderBy(e => e.Name).ToList();
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
            // Store outside wwwroot so files are not directly browsable.
            var uploads = Path.Combine(_environment.ContentRootPath, "uploads", "tickets", ticket.Id.ToString());
            Directory.CreateDirectory(uploads);

            foreach (var photo in uploadedPhotos)
            {
                var extension = DetectImageExtension(photo)!;
                var fileName = Guid.NewGuid().ToString("N") + extension;
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

    [HttpGet]
    public async Task<IActionResult> Photo(int id)
    {
        var photo = await _dbContext.TicketPhotos
            .Include(p => p.Ticket)
            .FirstOrDefaultAsync(p => p.Id == id);
        if (photo?.Ticket is null)
        {
            return NotFound();
        }

        var user = await _userManager.GetUserAsync(User);
        if (user is null || !CanAccessTicket(photo.Ticket, user))
        {
            // Do not reveal whether the photo exists.
            return NotFound();
        }

        var uploadsRoot = Path.GetFullPath(Path.Combine(_environment.ContentRootPath, "uploads"));
        var fullPath = Path.GetFullPath(Path.Combine(_environment.ContentRootPath, photo.FilePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!fullPath.StartsWith(uploadsRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !System.IO.File.Exists(fullPath))
        {
            return NotFound();
        }

        var contentType = photo.FilePath.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
            ? "image/png"
            : "image/jpeg";

        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.Headers["Content-Disposition"] = "inline";

        return PhysicalFile(fullPath, contentType);
    }

    private static string? DetectImageExtension(IFormFile file)
    {
        if (file is null || file.Length < 8)
        {
            return null;
        }

        Span<byte> header = stackalloc byte[8];
        using var stream = file.OpenReadStream();
        var read = stream.Read(header);
        if (read < 8)
        {
            return null;
        }

        // JPEG: FF D8 FF
        if (header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF)
        {
            return ".jpg";
        }

        // PNG: 89 50 4E 47 0D 0A 1A 0A
        if (header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47
            && header[4] == 0x0D && header[5] == 0x0A && header[6] == 0x1A && header[7] == 0x0A)
        {
            return ".png";
        }

        return null;
    }
}
