using Microsoft.AspNetCore.Identity;
using appdevPUPSCOUT.Models;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.ComponentModel.DataAnnotations;
using appdevPUPSCOUT.Services;

namespace appdevPUPSCOUT.Areas.Identity.Pages.Account;

public class ForgotPasswordModel(
    UserManager<ApplicationUser> userManager,
    IEmailSender emailSender,
    OtpService otpService,
    IConfiguration configuration,
    IEmailQueue emailQueue) : PageModel
{
    private readonly UserManager<ApplicationUser> _userManager = userManager;
    private readonly IEmailSender _emailSender = emailSender;
    private readonly OtpService _otpService = otpService;
    private readonly IConfiguration _configuration = configuration;
    private readonly IEmailQueue _emailQueue = emailQueue;

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public class InputModel
    {
        [Required]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;
    }

    public void OnGet() { }

    private bool IsAllowedEmail(string email)
    {
        email = (email ?? string.Empty).Trim().ToLowerInvariant();
        if (email.Contains(' ')) return false;
        var firstAt = email.IndexOf('@');
        var lastAt = email.LastIndexOf('@');
        if (firstAt <= 0 || firstAt != lastAt) return false;
        var domain = email[(lastAt + 1)..];

        var allowed = _configuration.GetSection("Registration:AllowedEmailDomains").Get<string[]>() ?? Array.Empty<string>();
        return allowed.Any(d => string.Equals(d.Trim().ToLowerInvariant(), domain, StringComparison.Ordinal));
    }

    public async Task<IActionResult> OnPostAsync()
    {
        Input.Email = (Input.Email ?? string.Empty).Trim().ToLowerInvariant();

        if (!ModelState.IsValid) return Page();

        if (!IsAllowedEmail(Input.Email))
        {
            ModelState.AddModelError("Input.Email", "Please use your PUP email address.");
            return Page();
        }

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        var (allowed, error, _) = await _otpService.CanRequestAsync(Input.Email, OtpService.PasswordReset, ip);
        if (!allowed)
        {
            TempData["Error"] = error;
            return RedirectToPage("ResetPassword", new { email = Input.Email });
        }

        var user = await _userManager.FindByEmailAsync(Input.Email);
        if (user is not null)
        {
            var (success, code, _) = await _otpService.GenerateAsync(Input.Email, OtpService.PasswordReset, ip);
            if (success && code is not null)
            {
                var queued = await _emailQueue.EnqueueAsync(new EmailMessage(
                    "PasswordResetOtp",
                    Input.Email,
                    "Reset your password",
                    $"Your password reset code is: <b>{code}</b>. It expires in 10 minutes."));
                if (!queued)
                {
                    TempData["Error"] = "Please try again in a moment.";
                }
            }
        }

        return RedirectToPage("ResetPassword", new { email = Input.Email });
    }
}
