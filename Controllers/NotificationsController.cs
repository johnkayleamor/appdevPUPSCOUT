using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using appdevPUPSCOUT.Data;
using appdevPUPSCOUT.Models;

namespace appdevPUPSCOUT.Controllers;

[Authorize]
public class NotificationsController(ApplicationDbContext dbContext, UserManager<ApplicationUser> userManager) : Controller
{
    private readonly ApplicationDbContext _dbContext = dbContext;
    private readonly UserManager<ApplicationUser> _userManager = userManager;

    public async Task<IActionResult> Index(int page = 1)
    {
        const int pageSize = 20;
        var user = await _userManager.GetUserAsync(User);
        if (user is null) return Challenge();

        var query = _dbContext.Notifications
            .AsNoTracking()
            .Where(n => n.UserId == user.Id)
            .OrderByDescending(n => n.CreatedAt);

        var total = await query.CountAsync();
        var notifications = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

        ViewBag.Page = page;
        ViewBag.TotalPages = (int)Math.Ceiling(total / (double)pageSize);
        return View(notifications);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkRead(int id)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null) return Challenge();

        var notification = await _dbContext.Notifications.FirstOrDefaultAsync(n => n.Id == id && n.UserId == user.Id);
        if (notification is not null)
        {
            notification.IsRead = true;
            await _dbContext.SaveChangesAsync();
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkAllRead()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null) return Challenge();

        var unread = await _dbContext.Notifications.Where(n => n.UserId == user.Id && !n.IsRead).ToListAsync();
        foreach (var notification in unread)
        {
            notification.IsRead = true;
        }
        await _dbContext.SaveChangesAsync();

        return RedirectToAction(nameof(Index));
    }
}
