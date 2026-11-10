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

    [EnableRateLimiting("ReportSubmission")]
    public IActionResult Create()
    {
        ViewBag.Rooms = _dbContext.Rooms.OrderBy(r => r.Name).ToList();
        ViewBag.Equipment = _dbContext.Equipment.OrderBy(e => e.Name).ToList();
        return View(new TicketCreateViewModel());
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
            ViewBag.Rooms = _dbContext.Rooms.OrderBy(r => r.Name).ToList();
            ViewBag.Equipment = _dbContext.Equipment.OrderBy(e => e.Name).ToList();
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
