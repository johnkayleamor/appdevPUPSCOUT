using System.ComponentModel.DataAnnotations;

namespace PupScout.Models
{
    public class RegisterViewModel
    {
        [Required(ErrorMessage = "Enter your full name.")]
        [Display(Name = "Full Name")]
        public string FullName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Enter your PUP webmail.")]
        [RegularExpression(@"^[^\s@]+@(iskolarngbayan\.)?pup\.edu\.ph$",
            ErrorMessage = "Use your @pup.edu.ph or @iskolarngbayan.pup.edu.ph email.")]
        [Display(Name = "PUP Webmail")]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "Enter your student or employee ID.")]
        [RegularExpression(@"^\d{4}-\d{5}-[A-Za-z]{2}-\d$",
            ErrorMessage = "Use the format 2026-00000-MN-0.")]
        [Display(Name = "Student / Employee ID")]
        public string IdNumber { get; set; } = string.Empty;

        [Required(ErrorMessage = "Select your role.")]
        public string Role { get; set; } = "Student";

        [Required(ErrorMessage = "Enter your college or department.")]
        [Display(Name = "College / Department")]
        public string College { get; set; } = string.Empty;

        [Required(ErrorMessage = "Enter a password.")]
        [MinLength(8, ErrorMessage = "Password must be at least 8 characters.")]
        [DataType(DataType.Password)]
        public string Password { get; set; } = string.Empty;

        [Required(ErrorMessage = "Confirm your password.")]
        [Compare(nameof(Password), ErrorMessage = "Passwords do not match.")]
        [DataType(DataType.Password)]
        [Display(Name = "Confirm Password")]
        public string ConfirmPassword { get; set; } = string.Empty;
    }
}