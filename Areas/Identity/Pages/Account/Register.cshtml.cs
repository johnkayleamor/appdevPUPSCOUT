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
    OtpService otpService,
    IConfiguration configuration,
    IEmailQueue emailQueue) : PageModel
{
    private readonly UserManager<ApplicationUser> _userManager = userManager;
    private readonly SignInManager<ApplicationUser> _signInManager = signInManager;
    private readonly IEmailSender _emailSender = emailSender;
    private readonly RoleManager<IdentityRole> _roleManager = roleManager;
    private readonly OtpService _otpService = otpService;
    private readonly IConfiguration _configuration = configuration;
    private readonly IEmailQueue _emailQueue = emailQueue;

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

        Input.Email = NormalizeEmail(Input.Email);

        if (!IsAllowedEmail(Input.Email))
        {
            ModelState.AddModelError("Input.Email", "Please use your PUP email address.");
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
                var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
                var (allowed, rateError, _) = await _otpService.CanRequestAsync(Input.Email, OtpService.EmailConfirmation, ip);
                if (!allowed)
                {
                    CodeSent = true;
                    ModelState.AddModelError(string.Empty, rateError!);
                    return Page();
                }

                var existingUser = await _userManager.FindByEmailAsync(Input.Email);
                if (existingUser is not null)
                {
                    await _emailQueue.EnqueueAsync(new EmailMessage(
                        "AccountAlreadyExists",
                        Input.Email,
                        "Account already exists",
                        "An account with this email already exists. If this was not you, please contact support."));
                }
                else
                {
                    var (success, code, error) = await _otpService.GenerateAsync(Input.Email, OtpService.EmailConfirmation, ip);
                    if (success && code is not null)
                    {
                        var queued = await _emailQueue.EnqueueAsync(new EmailMessage(
                            "EmailConfirmationOtp",
                            Input.Email,
                            "Confirm your email",
                            $"Your verification code is: <b>{code}</b>. It expires in 10 minutes."));
                        if (!queued)
                        {
                            CodeSent = true;
                            ModelState.AddModelError(string.Empty, "Please try again in a moment.");
                            return Page();
                        }
                    }
                    else if (!string.IsNullOrWhiteSpace(error))
                    {
                        CodeSent = true;
                        ModelState.AddModelError(string.Empty, error);
                        return Page();
                    }
                }

                CodeSent = true;
                ModelState.AddModelError(string.Empty, "If eligible, a verification code has been sent to your PUP webmail.");
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

    private string NormalizeEmail(string email) => (email ?? string.Empty).Trim().ToLowerInvariant();

    private bool IsAllowedEmail(string email)
    {
        email = NormalizeEmail(email);
        if (email.Contains(' ')) return false;
        var firstAt = email.IndexOf('@');
        var lastAt = email.LastIndexOf('@');
        if (firstAt <= 0 || firstAt != lastAt) return false;
        var domain = email[(lastAt + 1)..];

        var allowed = _configuration.GetSection("Registration:AllowedEmailDomains").Get<string[]>() ?? Array.Empty<string>();
        return allowed.Any(d => string.Equals(d.Trim().ToLowerInvariant(), domain, StringComparison.Ordinal));
    }
}
