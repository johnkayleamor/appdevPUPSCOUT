using Microsoft.AspNetCore.Identity.UI.Services;

namespace appdevPUPSCOUT.Services;

public class SimulatedEmailSender(ILogger<SimulatedEmailSender> logger) : IEmailSender, IAppEmailService
{
    public Task SendEmailAsync(string email, string subject, string htmlMessage)
    {
        try
        {
            logger.LogInformation("EMAIL to {Email}: {Subject}\n{Message}", email, subject, htmlMessage);

            var path = Path.Combine(Directory.GetCurrentDirectory(), "emails.txt");
            File.AppendAllText(path, $"To: {email}\nSubject: {subject}\n{htmlMessage}\n---\n");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to write simulated email.");
        }

        return Task.CompletedTask;
    }
}
