using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using appdevPUPSCOUT.Data;
using appdevPUPSCOUT.Models;

namespace appdevPUPSCOUT.Services;

public class NotificationService(ApplicationDbContext dbContext, UserManager<ApplicationUser> userManager, IEmailQueue emailQueue, ILogger<NotificationService> logger)
{
    private readonly ApplicationDbContext _dbContext = dbContext;
    private readonly UserManager<ApplicationUser> _userManager = userManager;
    private readonly IEmailQueue _emailQueue = emailQueue;
    private readonly ILogger<NotificationService> _logger = logger;

    public async Task NotifyAsync(string userId, int? ticketId, string trackingNumber, string message, string? link, bool sendEmail = true)
    {
        _dbContext.Notifications.Add(new Notification
        {
            UserId = userId,
            TicketId = ticketId,
            Message = message,
            Link = link,
            CreatedAt = DateTime.UtcNow
        });
        await _dbContext.SaveChangesAsync();

        if (!sendEmail) return;

        var user = await _userManager.FindByIdAsync(userId);
        if (user?.Email is not null)
        {
            var body = string.IsNullOrWhiteSpace(link)
                ? message
                : $"{message}\n\nView ticket: {link}";
            await _emailQueue.EnqueueAsync(new EmailMessage(
                "TicketNotification",
                user.Email,
                $"Ticket {trackingNumber}",
                body));
        }
    }

    public async Task NotifyAdminsAsync(int? ticketId, string trackingNumber, string message, string? link, bool sendEmail = true)
    {
        var admins = await _userManager.GetUsersInRoleAsync("Admin");
        foreach (var admin in admins)
        {
            await NotifyAsync(admin.Id, ticketId, trackingNumber, message, link, sendEmail);
        }
    }

    public async Task NotifyAdminsAsync(int? ticketId, string trackingNumber, string message, string? link)
    {
        var admins = await _userManager.GetUsersInRoleAsync("Admin");
        foreach (var admin in admins)
        {
            await NotifyAsync(admin.Id, ticketId, trackingNumber, message, link);
        }
    }
}
