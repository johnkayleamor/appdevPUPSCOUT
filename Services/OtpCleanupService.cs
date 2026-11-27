using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using appdevPUPSCOUT.Data;

namespace appdevPUPSCOUT.Services;

public sealed class OtpCleanupService(IServiceScopeFactory scopeFactory, IConfiguration configuration, ILogger<OtpCleanupService> logger) : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory = scopeFactory;
    private readonly IConfiguration _configuration = configuration;
    private readonly ILogger<OtpCleanupService> _logger = logger;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                var ageDays = _configuration.GetValue<int>("Otp:CleanupAgeDays", 2);
                var cutoff = DateTime.UtcNow.AddDays(-ageDays);

                var deleted = await dbContext.OtpCodes
                    .Where(o => o.CreatedAt < cutoff)
                    .ExecuteDeleteAsync(stoppingToken);

                _logger.LogInformation("Deleted {Count} OTP code(s) older than {Days} days.", deleted, ageDays);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "OTP cleanup failed.");
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
}