namespace appdevPUPSCOUT.Services;

public interface IAppEmailService
{
    Task SendEmailAsync(string to, string subject, string htmlBody);
}
