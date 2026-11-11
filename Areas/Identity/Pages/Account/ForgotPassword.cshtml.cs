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
    OtpService otpService) : PageModel
{
    private readonly UserManager<ApplicationUser> _userManager = userManager;
    private readonly IEmailSender _emailSender = emailSender;
    private readonly OtpService _otpService = otpService;

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public class InputModel
    {
        [Required]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;
    }

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid) return Page();

        if (!Input.Email.EndsWith("@pup.edu.ph", StringComparison.OrdinalIgnoreCase))
        {
            ModelState.AddModelError("Input.Email", "Only @pup.edu.ph email addresses are allowed.");
            return Page();
        }

        var user = await _userManager.FindByEmailAsync(Input.Email);
        if (user is not null)
        {
            var otp = await _otpService.GenerateAsync(Input.Email, OtpService.PasswordReset);
            await _emailSender.SendEmailAsync(
                Input.Email,
                "Reset your password",
                $"Your password reset code is: <b>{otp}</b>. It expires in 10 minutes.");
        }

        return RedirectToPage("ResetPassword", new { email = Input.Email });
    }
}
