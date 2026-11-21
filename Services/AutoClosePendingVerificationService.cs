using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using appdevPUPSCOUT.Data;
using appdevPUPSCOUT.Models;

namespace appdevPUPSCOUT.Services;

public class AutoClosePendingVerificationService(IServiceScopeFactory scopeFactory, ILogger<AutoClosePendingVerificationService> logger) : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory = scopeFactory;
    private readonly ILogger<AutoClosePendingVerificationService> _logger = logger;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CloseExpiredAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Auto-close pending verification failed.");
            }

            try
            {
                await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task CloseExpiredAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var cutoff = DateTime.UtcNow.AddHours(-72);

        var pending = await dbContext.Tickets
            .Include(t => t.StatusHistory)
            .Where(t => t.Status == TicketStatus.PendingVerification)
            .ToListAsync(cancellationToken);

        foreach (var ticket in pending)
        {
            var lastPending = ticket.StatusHistory
                .Where(h => h.Status == TicketStatus.PendingVerification)
                .OrderByDescending(h => h.ChangedAt)
                .FirstOrDefault();

            var since = lastPending?.ChangedAt ?? ticket.CreatedAt;
            if (since > cutoff) continue;

            ticket.Status = TicketStatus.Closed;

            dbContext.TicketStatusHistories.Add(new TicketStatusHistory
            {
                TicketId = ticket.Id,
                Status = TicketStatus.Closed,
                ChangedByUserId = null,
                ChangedAt = DateTime.UtcNow
            });

            await dbContext.SaveChangesAsync(cancellationToken);

            var auditService = scope.ServiceProvider.GetRequiredService<AuditService>();
            await auditService.Log(null, "AutoClose", ticket.Id,
                $"Ticket {ticket.TrackingNumber} auto-closed after 72 hours without reporter response.",
                ticket.ReporterId, null, "System");
        }
    }
}
