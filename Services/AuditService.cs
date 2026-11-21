using appdevPUPSCOUT.Data;
using appdevPUPSCOUT.Models;

namespace appdevPUPSCOUT.Services;

public class AuditService(ApplicationDbContext dbContext)
{
    private readonly ApplicationDbContext _dbContext = dbContext;

    public Task LogAsync(
        string? actorId,
        string action,
        int? ticketId = null,
        string? details = null,
        string? targetUserId = null,
        string? targetUserEmail = null,
        string? actorName = null)
    {
        return LogInternalAsync(actorId, action, ticketId, details, targetUserId, targetUserEmail, actorName);
    }

    public Task Log(
        string? actorId,
        string action,
        int? ticketId = null,
        string? details = null,
        string? targetUserId = null,
        string? targetUserEmail = null,
        string? actorName = null)
    {
        return LogInternalAsync(actorId, action, ticketId, details, targetUserId, targetUserEmail, actorName);
    }

    public Task Log(string? actorId, string action, string details)
        => LogInternalAsync(actorId, action, null, details, null, null, null);

    public Task Log(string? actorId, string action, int ticketId, string details)
        => LogInternalAsync(actorId, action, ticketId, details, null, null, null);

    private async Task LogInternalAsync(
        string? actorId,
        string action,
        int? ticketId,
        string? details,
        string? targetUserId,
        string? targetUserEmail,
        string? actorName)
    {
        _dbContext.AuditLogs.Add(new AuditLog
        {
            ActorId = actorId,
            AdminId = actorId,
            Action = action,
            TicketId = ticketId,
            Details = details,
            Reason = details ?? string.Empty,
            TargetUserId = targetUserId,
            TargetUserEmail = targetUserEmail,
            AdminName = actorName,
            CreatedAt = DateTime.UtcNow
        });

        await _dbContext.SaveChangesAsync();
    }
}
