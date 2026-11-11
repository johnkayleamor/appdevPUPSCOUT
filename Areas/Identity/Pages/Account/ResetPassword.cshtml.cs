using Microsoft.AspNetCore.Identity;
using appdevPUPSCOUT.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.ComponentModel.DataAnnotations;
using appdevPUPSCOUT.Services;

namespace appdevPUPSCOUT.Areas.Identity.Pages.Account;

public class ResetPasswordModel(
    UserManager<ApplicationUser> userManager,
    OtpService otpService) : PageModel
{
    private readonly UserManager<ApplicationUser> _userManager = userManager;
    private readonly OtpService _otpService = otpService;

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public class InputModel
    {
        [Required]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Required]
        [StringLength(6, MinimumLength = 6)]
        [Display(Name = "Reset code")]
        public string Code { get; set; } = string.Empty;

        [Required]
        [StringLength(100, MinimumLength = 6)]
        [DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;

        [DataType(DataType.Password)]
        [Compare("Password", ErrorMessage = "The password and confirmation password do not match.")]
        public string ConfirmPassword { get; set; } = string.Empty;
    }

    public void OnGet(string? email = null)
    {
        Input.Email = email ?? string.Empty;
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid) return Page();

        var user = await _userManager.FindByEmailAsync(Input.Email);
        if (user is null)
        {
            ModelState.AddModelError(string.Empty, "Invalid request.");
            return Page();
        }

        var valid = await _otpService.VerifyAsync(Input.Email, OtpService.PasswordReset, Input.Code);
        if (!valid)
        {
            ModelState.AddModelError(string.Empty, "Invalid or expired reset code.");
            return Page();
        }

        var token = await _userManager.GeneratePasswordResetTokenAsync(user);
        var result = await _userManager.ResetPasswordAsync(user, token, Input.Password);
        if (result.Succeeded)
        {
            return RedirectToPage("ResetPasswordConfirmation");
        }

        foreach (var error in result.Errors)
        {
            ModelState.AddModelError(string.Empty, error.Description);
        }
        return Page();
    }
}
