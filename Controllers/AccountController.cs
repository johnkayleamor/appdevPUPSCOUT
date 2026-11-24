using Microsoft.AspNetCore.Mvc;
using PupScout.Models;

namespace PupScout.Controllers
{
    public class AccountController : Controller
    {
        [HttpGet]
        public IActionResult Login() => View();

        [HttpGet]
        public IActionResult Register() => View(new RegisterViewModel());

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Register(RegisterViewModel model)
        {
            if (!ModelState.IsValid)
                return View(model);

            // TODO: hash the password and save the user (e.g., ASP.NET Identity or your own DbContext).
            // Check for duplicate Email / IdNumber before saving.

            return RedirectToAction(nameof(Login));
        }
    }
}