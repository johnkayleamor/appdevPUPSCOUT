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
    IWebHostEnvironment environment) : Controller
{
    private readonly ApplicationDbContext _dbContext = dbContext;
    private readonly UserManager<ApplicationUser> _userManager = userManager;
    private readonly IWebHostEnvironment _environment = environment;

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
        ViewBag.Rooms = _dbContext.Rooms.Include(r => r.Floor).ThenInclude(f => f.Building).Where(r => r.IsActive).OrderBy(r => r.Name).ToList();
        ViewBag.Equipment = _dbContext.Equipment.Include(e => e.Room).ThenInclude(r => r.Floor).ThenInclude(f => f.Building).Where(e => e.Room != null && e.Room!.IsActive).OrderBy(e => e.Name).ToList();
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

    public async Task<IActionResult> MyReports()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null) return Challenge();

        var tickets = await _dbContext.Tickets
            .AsNoTracking()
            .Include(t => t.Room).ThenInclude(r => r.Floor).ThenInclude(f => f.Building)
            .Include(t => t.Equipment)
            .Where(t => t.ReporterId == user.Id)
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync();

        return View(tickets);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("ReportSubmission")]
    public async Task<IActionResult> Create(TicketCreateViewModel model)
    {
        if (model.Image is not null)
        {
            if (!model.Image.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            {
                ModelState.AddModelError(nameof(model.Image), "Only image files are allowed.");
            }

            if (model.Image.Length > 5 * 1024 * 1024)
            {
                ModelState.AddModelError(nameof(model.Image), "Image must be 5 MB or smaller.");
            }
        }

        if (!ModelState.IsValid)
        {
            ViewBag.Buildings = _dbContext.Buildings.OrderBy(b => b.Name).ToList();
            ViewBag.Floors = _dbContext.Floors.Include(f => f.Building).OrderBy(f => f.Name).ToList();
            ViewBag.Rooms = _dbContext.Rooms.Include(r => r.Floor).ThenInclude(f => f.Building).Where(r => r.IsActive).OrderBy(r => r.Name).ToList();
            ViewBag.Equipment = _dbContext.Equipment.Include(e => e.Room).ThenInclude(r => r.Floor).ThenInclude(f => f.Building).Where(e => e.Room != null && e.Room!.IsActive).OrderBy(e => e.Name).ToList();
            return View(model);
        }

        var user = await _userManager.GetUserAsync(User);
        if (user is null)
        {
            return Challenge();
        }

        var ticket = new Ticket
        {
            TrackingNumber = Guid.NewGuid().ToString("N")[..12].ToUpperInvariant(),
            Category = model.Category,
            RoomId = model.RoomId,
            EquipmentId = model.EquipmentId,
            Description = model.Description,
            ReporterId = user.Id,
            Status = TicketStatus.Pending
        };

        _dbContext.Tickets.Add(ticket);
        await _dbContext.SaveChangesAsync();

        if (model.Image is not null)
        {
            var uploads = Path.Combine(_environment.WebRootPath, "uploads");
            Directory.CreateDirectory(uploads);
            var fileName = ticket.Id + Path.GetExtension(model.Image.FileName);
            var filePath = Path.Combine(uploads, fileName);
            await using var stream = System.IO.File.Create(filePath);
            await model.Image.CopyToAsync(stream);
        }

        TempData["Message"] = $"Report submitted. Tracking: {ticket.TrackingNumber}";
        return RedirectToAction("Index", "Home");
    }
}
