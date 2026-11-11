using Microsoft.AspNetCore.Identity;
using appdevPUPSCOUT.Models;
using appdevPUPSCOUT.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;
using System.ComponentModel.DataAnnotations;

namespace appdevPUPSCOUT.Areas.Identity.Pages.Account;

public class VerifyEmailModel(
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager,
    OtpService otpService) : PageModel
{
    private readonly UserManager<ApplicationUser> _userManager = userManager;
    private readonly SignInManager<ApplicationUser> _signInManager = signInManager;
    private readonly OtpService _otpService = otpService;

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public string? ReturnUrl { get; set; }

    public class InputModel
    {
        [Required]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Required]
        [StringLength(6, MinimumLength = 6)]
        [Display(Name = "Verification code")]
        public string Code { get; set; } = string.Empty;
    }

    public void OnGet(string? email = null, string? returnUrl = null)
    {
        Input.Email = email ?? string.Empty;
        ReturnUrl = returnUrl;
    }

    public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
    {
        returnUrl ??= Url.Content("~/");
        if (!ModelState.IsValid) return Page();

        var valid = await _otpService.VerifyAsync(Input.Email, OtpService.EmailConfirmation, Input.Code);
        if (!valid)
        {
            ModelState.AddModelError(string.Empty, "Invalid or expired verification code.");
            return Page();
        }

        var user = await _userManager.FindByEmailAsync(Input.Email);
        if (user is null)
        {
            ModelState.AddModelError(string.Empty, "User not found.");
            return Page();
        }

        user.EmailConfirmed = true;
        await _userManager.UpdateAsync(user);

        await _signInManager.SignInAsync(user, isPersistent: false);
        return LocalRedirect(returnUrl);
    }
}
