using Microsoft.AspNetCore.Http;

namespace FypWeb.ViewModels
{
    public class LoginViewModel
    {
        public string Role { get; set; } = "Client"; // Client | Worker | Company | Police
        public string EmailOrCnic { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
    }

    public class ExperienceInput
    {
        public string? WorkAt { get; set; }
        public string? ExpDetail { get; set; }
        public string? Duration { get; set; }
        public int? CategoryId { get; set; }
        public int? SkillsId { get; set; }
    }

    public class SignupViewModel
    {
        public string Role { get; set; } = "Client"; // Client | Worker | Company | Police

        // Shared / Client / Worker
        public string? Name { get; set; }
        public string? Email { get; set; }
        public string? Phone { get; set; }
        public string? Address { get; set; }
        public string? Password { get; set; }
        public string? ConfirmPassword { get; set; }
        public IFormFile? PictureFile { get; set; }
        public double? Latitude { get; set; }
        public double? Longitude { get; set; }

        // Worker-only
        public string? Cnic { get; set; }
        public int? Age { get; set; }
        public decimal? Salary { get; set; }
        public string? Gender { get; set; }
        public string? Bio { get; set; }
        public string? ExperiencesJson { get; set; }
        public List<int> HabitIds { get; set; } = new();

        // Company-only
        public string? CompanyName { get; set; }
        public string? LicenseNumber { get; set; }

        // Police-only
        public string? StationName { get; set; }
        public string? BadgeID { get; set; }
        public string? JurisdictionAddress { get; set; }
    }
}
