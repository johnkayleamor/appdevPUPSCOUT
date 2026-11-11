using Microsoft.AspNetCore.Identity;
using appdevPUPSCOUT.Models;
using appdevPUPSCOUT.Services;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;
using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Text.Encodings.Web;

namespace appdevPUPSCOUT.Areas.Identity.Pages.Account;

public class RegisterModel(
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager,
    IEmailSender emailSender,
    RoleManager<IdentityRole> roleManager,
    OtpService otpService) : PageModel
{
    private readonly UserManager<ApplicationUser> _userManager = userManager;
    private readonly SignInManager<ApplicationUser> _signInManager = signInManager;
    private readonly IEmailSender _emailSender = emailSender;
    private readonly RoleManager<IdentityRole> _roleManager = roleManager;
    private readonly OtpService _otpService = otpService;

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public string? ReturnUrl { get; set; }

    public bool CodeSent { get; set; }

    public class InputModel
    {
        [Required]
        [EmailAddress]
        [Display(Name = "Email")]
        public string Email { get; set; } = string.Empty;

        [Required]
        [StringLength(100, MinimumLength = 6)]
        [DataType(DataType.Password)]
        [Display(Name = "Password")]
        public string Password { get; set; } = string.Empty;

        [DataType(DataType.Password)]
        [Display(Name = "Confirm password")]
        [Compare("Password", ErrorMessage = "The password and confirmation password do not match.")]
        public string ConfirmPassword { get; set; } = string.Empty;

        [Required]
        [Display(Name = "Role")]
        public string Role { get; set; } = "Student";

        [Display(Name = "Verification code")]
        public string? Code { get; set; }
    }

    public void OnGet(string? returnUrl = null)
    {
        ReturnUrl = returnUrl;
    }

    public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
    {
        returnUrl ??= Url.Content("~/");

        if (!Input.Email.EndsWith("@pup.edu.ph", StringComparison.OrdinalIgnoreCase))
        {
            ModelState.AddModelError("Input.Email", "Only @pup.edu.ph email addresses are allowed.");
        }

        if (Input.Role is not "Student" and not "Teacher")
        {
            ModelState.AddModelError("Input.Role", "Invalid role selection.");
        }

        CodeSent = !string.IsNullOrWhiteSpace(Input.Code);

        if (ModelState.IsValid)
        {
            if (string.IsNullOrWhiteSpace(Input.Code))
            {
                if (await _userManager.FindByEmailAsync(Input.Email) is not null)
                {
                    ModelState.AddModelError(string.Empty, "An account with this email already exists.");
                    return Page();
                }

                var otp = await _otpService.GenerateAsync(Input.Email, OtpService.EmailConfirmation);

                await _emailSender.SendEmailAsync(
                    Input.Email,
                    "Confirm your email",
                    $"Your verification code is: <b>{otp}</b>. It expires in 10 minutes.");

                CodeSent = true;
                ModelState.AddModelError(string.Empty, "A verification code has been sent to your email.");
                return Page();
            }

            var valid = await _otpService.VerifyAsync(Input.Email, OtpService.EmailConfirmation, Input.Code);
            if (!valid)
            {
                CodeSent = true;
                ModelState.AddModelError(string.Empty, "Invalid or expired verification code.");
                return Page();
            }

            var user = new ApplicationUser { UserName = Input.Email, Email = Input.Email, EmailConfirmed = true };

            var result = await _userManager.CreateAsync(user, Input.Password);
            if (result.Succeeded)
            {
                if (!await _roleManager.RoleExistsAsync(Input.Role))
                {
                    await _roleManager.CreateAsync(new IdentityRole(Input.Role));
                }

                await _userManager.AddToRoleAsync(user, Input.Role);

                await _signInManager.SignInAsync(user, isPersistent: false);
                return LocalRedirect(returnUrl);
            }

            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }
        }

        CodeSent = CodeSent || !string.IsNullOrWhiteSpace(Input.Code);
        return Page();
    }
}
