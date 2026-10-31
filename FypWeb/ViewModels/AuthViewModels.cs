using Microsoft.AspNetCore.Http;

namespace FypWeb.ViewModels
{
    public class LoginViewModel
    {
        public string Role { get; set; } = "Client"; // Client | Worker | Company | Police
        public string EmailOrCnic { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public bool RememberMe { get; set; }
    }

    // Work history row — mirrors the NEW backend contract: experiencesJson now
    // carries ONLY history fields (no category/skill ids; junctions come from
    // skillsJson / SkillInput).
    public class ExperienceInput
    {
        public string? WorkAt { get; set; }
        public string? ExpDetail { get; set; }
        public string? Duration { get; set; }
    }

    // Worker_Category junction row — mirrors the backend's new skillsJson
    // ([{ CategoryId, SkillsId }] deserialized as List<WorkerCategory>).
    // CategoryName/SkillName are display-only (never persisted).
    public class SkillInput
    {
        public int CategoryId { get; set; }
        public int SkillsId { get; set; }
        public string? CategoryName { get; set; }
        public string? SkillName { get; set; }
        public bool IsPrimary { get; set; }
    }

    /// <summary>
    /// Edit-profile form for the CLIENT role. Mirrors the fields the RN app
    /// posts to api/AccountCreation/UpdateClient: ClientId (from the auth
    /// cookie here), Name, Phone, Address, Email, optional Password and an
    /// optional new PictureFile. Location is NOT editable here — RN only sends
    /// Latitude/Longitude on signup, and UpdateClient never touches them.
    /// </summary>
    public class EditClientProfileVM
    {
        public string? Name { get; set; }
        public string? Email { get; set; }
        public string? Phone { get; set; }
        public string? Address { get; set; }
        public string? Password { get; set; }
        public string? ConfirmPassword { get; set; }
        public IFormFile? PictureFile { get; set; }

        // Not posted — current photo for the preview ("" = fallback initial).
        public string? CurrentPicture { get; set; }
    }

    /// <summary>
    /// Edit-profile form for the WORKER role. Mirrors the fields the RN app
    /// posts to api/AccountCreation/UpdateWorker: Name, Phone, Address, Age,
    /// Salary, Gender, Bio, optional Password, optional new PictureFile,
    /// experiencesJson (full replace) and habitsJson (sync).
    /// Cnic is displayed read-only: the API uses it only as the image-file
    /// identifier and never updates the column (it is the worker's login key).
    /// </summary>
    public class EditWorkerProfileVM
    {
        public string? Name { get; set; }
        public string? Cnic { get; set; }        // read-only display
        public string? Phone { get; set; }
        public string? Address { get; set; }
        public int? Age { get; set; }
        public decimal? Salary { get; set; }
        public string? Gender { get; set; }
        public string? Bio { get; set; }
        public string? Password { get; set; }
        public string? ConfirmPassword { get; set; }
        public IFormFile? PictureFile { get; set; }

        // Round-trips the two separate lists managed on the Add Skills screen
        // (same split as the RN app / new API contract):
        // ExperiencesJson = [{ WorkAt, ExpDetail, Duration }] (work history),
        // SkillsJson      = [{ CategoryId, SkillsId }] (service skills).
        public string? ExperiencesJson { get; set; }
        public string? SkillsJson { get; set; }

        // Checked habit ids — mirrors habitsJson in UpdateWorker: an empty
        // list clears the worker's habits, exactly like the RN edit form.
        public List<int> HabitIds { get; set; } = new();

        // Not posted — current photo for the preview.
        public string? CurrentPicture { get; set; }
    }

    /// <summary>
    /// Edit-profile form for the COMPANY role. Mirrors the fields the RN app
    /// posts to api/AccountCreation/UpdateCompany: CompanyID (from the auth
    /// cookie here), CompanyName, PhoneNo, CompanyAddress, LicenseNumber,
    /// Email, optional Password and an optional new LogoFile.
    /// </summary>
    public class CompanyEditProfileVM
    {
        public string? CompanyName { get; set; }
        public string? Email { get; set; }
        public string? Phone { get; set; }
        public string? Address { get; set; }
        public string? LicenseNumber { get; set; }
        public string? Password { get; set; }
        public string? ConfirmPassword { get; set; }
        public IFormFile? LogoFile { get; set; }

        // Not posted — current logo for the preview.
        public string? CurrentPicture { get; set; }
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
        // [{ WorkAt, ExpDetail, Duration }] — work history only
        public string? ExperiencesJson { get; set; }
        // [{ CategoryId, SkillsId }] — Worker_Category junction rows
        public string? SkillsJson { get; set; }
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
