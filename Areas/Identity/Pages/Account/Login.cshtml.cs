using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using appdevPUPSCOUT.Models;

namespace appdevPUPSCOUT.Areas.Identity.Pages.Account
{
    public class LoginModel : PageModel
    {
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly ILogger<LoginModel> _logger;

        public LoginModel(
            SignInManager<ApplicationUser> signInManager,
            UserManager<ApplicationUser> userManager,
            ILogger<LoginModel> logger)
        {
            _signInManager = signInManager;
            _userManager = userManager;
            _logger = logger;
        }

        [BindProperty]
        public InputModel Input { get; set; } = new();

        public string? ReturnUrl { get; set; }

        public class InputModel
        {
            [Required(ErrorMessage = "Enter your PUP webmail.")]
            [EmailAddress(ErrorMessage = "Enter a valid email address.")]
            public string Email { get; set; } = string.Empty;

            [Required(ErrorMessage = "Enter your password.")]
            [DataType(DataType.Password)]
            public string Password { get; set; } = string.Empty;

            [Required(ErrorMessage = "Select your role.")]
            public string Role { get; set; } = string.Empty;
        }

        public void OnGet(string? returnUrl = null)
        {
            ReturnUrl = returnUrl;
        }

        public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
        {
            returnUrl ??= Url.Content("~/");
            ReturnUrl = returnUrl;

            if (!ModelState.IsValid)
                return Page();

            var user = await _userManager.FindByEmailAsync(Input.Email);

            if (user != null)
            {
                var check = await _signInManager.CheckPasswordSignInAsync(
                    user, Input.Password, lockoutOnFailure: true);

                if (check.IsLockedOut)
                {
                    ModelState.AddModelError(string.Empty, "This account is locked. Try again later.");
                    return Page();
                }

                if (check.IsNotAllowed)
                {
                    ModelState.AddModelError(string.Empty, "This account is not allowed to sign in yet.");
                    return Page();
                }

                // The role picked on the form must match the role saved for the account.
                if (check.Succeeded && await _userManager.IsInRoleAsync(user, Input.Role))
                {
                    await _signInManager.SignInAsync(user, isPersistent: false);
                    _logger.LogInformation("User logged in.");
                    return LocalRedirect(returnUrl);
                }
            }

            // One message for every failure, so it doesn't reveal which part was wrong.
            ModelState.AddModelError(string.Empty, "Incorrect webmail, password or role.");
            return Page();
        }
    }
}
