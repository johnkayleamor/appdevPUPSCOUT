using Microsoft.AspNetCore.Identity.UI.Services;

namespace appdevPUPSCOUT.Services;

public class EmailSender(ILogger<EmailSender> logger) : IEmailSender
{
    public Task SendEmailAsync(string email, string subject, string htmlMessage)
    {
        logger.LogInformation("EMAIL to {Email}: {Subject}\n{Message}", email, subject, htmlMessage);

        var path = Path.Combine(Directory.GetCurrentDirectory(), "emails.txt");
        File.AppendAllText(path, $"To: {email}\nSubject: {subject}\n{htmlMessage}\n---\n");

        return Task.CompletedTask;
    }
}
