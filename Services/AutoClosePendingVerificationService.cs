using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using appdevPUPSCOUT.Data;
using appdevPUPSCOUT.Models;

namespace appdevPUPSCOUT.Services;

public class AutoClosePendingVerificationService(IServiceScopeFactory scopeFactory, IConfiguration configuration, ILogger<AutoClosePendingVerificationService> logger) : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory = scopeFactory;
    private readonly IConfiguration _configuration = configuration;
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
        var workflow = scope.ServiceProvider.GetRequiredService<TicketWorkflowService>();
        var hours = _configuration.GetValue<int>("Verification:TimeLimitHours", 72);
        var cutoff = DateTime.UtcNow.AddHours(-hours);

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

            await workflow.AutoCloseAsync(ticket.Id);
        }
    }
}
