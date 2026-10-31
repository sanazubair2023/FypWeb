using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text.RegularExpressions;
using Fyp_Backend.Models;
using FypWeb.Helpers;
using FypWeb.Services;
using FypWeb.ViewModels;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;

namespace FypWeb.Controllers;

public partial class AccountController : Controller
{
    private readonly Fyp1Context _context;
    private readonly ImageService _images;
    private readonly PasswordService _passwords;
    private readonly ILogger<AccountController> _logger;

    public AccountController(Fyp1Context context, ImageService images, PasswordService passwords,
        ILogger<AccountController> logger)
    {
        _context = context;
        _images = images;
        _passwords = passwords;
        _logger = logger;
    }

    [HttpGet]
    public IActionResult Login(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true) return RedirectToAction("Index", "Home");
        ViewBag.ReturnUrl = Url.IsLocalUrl(returnUrl) ? returnUrl : null;
        return View(new LoginViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("login")]
    public async Task<IActionResult> Login(LoginViewModel model, string? returnUrl = null)
    {
        var id = model.EmailOrCnic?.Trim() ?? "";
        var pwd = model.Password ?? "";
        ViewBag.ReturnUrl = Url.IsLocalUrl(returnUrl) ? returnUrl : null;
        if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(pwd))
        {
            TempData["Error"] = "Please enter your credentials.";
            return View(model);
        }

        ClaimsIdentity? identity = null;
        string? storedPassword = null;
        switch (model.Role)
        {
            case "Client":
                var client = await _context.Clients.FirstOrDefaultAsync(c => c.Email == id);
                if (client != null && _passwords.Verify(client.Password, pwd, out var clientRehash))
                {
                    if (clientRehash) { client.Password = _passwords.Hash(pwd); await _context.SaveChangesAsync(); }
                    storedPassword = client.Password;
                    identity = BuildIdentity(client.ClientId, "Client", client.Name, client.Picture, client.Email, storedPassword);
                }
                break;
            case "Worker":
                var worker = await _context.Workers.FirstOrDefaultAsync(w => w.Cnic == id);
                if (worker != null && _passwords.Verify(worker.Password, pwd, out var workerRehash))
                {
                    if (workerRehash) { worker.Password = _passwords.Hash(pwd); await _context.SaveChangesAsync(); }
                    storedPassword = worker.Password;
                    identity = BuildIdentity(worker.WorkerId, "Worker", worker.Name ?? "", worker.Picture, "", storedPassword);
                }
                break;
            case "Company":
                var company = await _context.Companies.FirstOrDefaultAsync(c => c.LicenseNumber == id);
                if (company != null && _passwords.Verify(company.Password, pwd, out var companyRehash))
                {
                    if (companyRehash) { company.Password = _passwords.Hash(pwd); await _context.SaveChangesAsync(); }
                    storedPassword = company.Password;
                    identity = BuildIdentity(company.CompanyID, "Company", company.CompanyName, company.CompanyPicture, company.Email, storedPassword);
                }
                break;
            case "Police":
                var police = await _context.PoliceOfficers.FirstOrDefaultAsync(p => p.BadgeID == id);
                if (police != null && _passwords.Verify(police.Password, pwd, out var policeRehash))
                {
                    if (policeRehash) { police.Password = _passwords.Hash(pwd); await _context.SaveChangesAsync(); }
                    storedPassword = police.Password;
                    identity = BuildIdentity(police.PoliceID, "Police", police.StationName, "", police.Email, storedPassword);
                }
                break;
        }

        if (identity == null)
        {
            TempData["Error"] = "Invalid credentials.";
            return View(model);
        }

        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity), new AuthenticationProperties { IsPersistent = model.RememberMe });
        if (Url.IsLocalUrl(returnUrl)) return LocalRedirect(returnUrl);
        return RedirectToAction("Index", "Home");
    }

    private static ClaimsIdentity BuildIdentity(int id, string role, string name, string? picture,
        string? email, string? storedPassword)
    {
        return new ClaimsIdentity(new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, id.ToString()),
            new(ClaimTypes.Name, string.IsNullOrWhiteSpace(name) ? role : name),
            new(ClaimTypes.Role, role),
            new("Picture", picture ?? ""),
            new("Email", email ?? ""),
            new("PasswordStamp", PasswordService.Stamp(storedPassword))
        }, CookieAuthenticationDefaults.AuthenticationScheme);
    }

    [HttpGet]
    [Authorize]
    public IActionResult AccessDenied() => View();

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction("Login");
    }

    [HttpGet]
    public async Task<IActionResult> Signup(string role = "Client")
    {
        await PopulateSignupLookups();
        return View(new SignupViewModel { Role = role });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Signup(SignupViewModel model)
    {
        await PopulateSignupLookups();
        if (!PasswordService.IsValidNewPassword(model.Password))
        {
            TempData["Error"] = "Password must be 8–100 characters and not whitespace only.";
            return View(model);
        }
        if (model.Password != model.ConfirmPassword)
        {
            TempData["Error"] = "Passwords do not match.";
            return View(model);
        }

        try
        {
            return model.Role switch
            {
                "Client" => await SignupClient(model),
                "Worker" => await SignupWorker(model),
                "Company" => await SignupCompany(model),
                _ => InvalidSignup(model, "Invalid role. Self-registration is not available for this account type.")
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Signup failed for role {Role}", model.Role);
            TempData["Error"] = "Account creation could not be completed. Check your information and try again.";
            return View(model);
        }
    }

    private IActionResult InvalidSignup(SignupViewModel model, string message)
    {
        TempData["Error"] = message;
        return View(model);
    }

    private async Task<IActionResult> SignupClient(SignupViewModel model)
    {
        if (!ValidEmail(model.Email) || string.IsNullOrWhiteSpace(model.Name) || !ValidPhone(model.Phone))
            return InvalidSignup(model, "A valid name, email address, and phone number are required.");
        if (!ValidCoordinates(model.Latitude, model.Longitude))
            return InvalidSignup(model, "Please select your location on the map before creating the account.");
        if (await _context.Clients.AnyAsync(c => c.Email == model.Email))
            return InvalidSignup(model, "Email is already registered.");

        var imagePath = await _images.SaveImageAsync(model.PictureFile);
        if (imagePath == "Invalid") return InvalidSignup(model, "Choose a valid JPG or PNG image up to 5 MB.");
        _context.Clients.Add(new Client
        {
            Name = model.Name!.Trim(),
            Email = model.Email!.Trim(),
            Phone = model.Phone!.Trim(),
            Password = _passwords.Hash(model.Password!),
            Address = model.Address?.Trim(),
            Latitude = model.Latitude,
            Longitude = model.Longitude,
            Picture = imagePath
        });
        await _context.SaveChangesAsync();
        TempData["Success"] = "Client registered successfully! Please log in.";
        return RedirectToAction("Login");
    }

    private async Task<IActionResult> SignupWorker(SignupViewModel model)
    {
        if (!ValidCnic(model.Cnic) || string.IsNullOrWhiteSpace(model.Name) || !ValidPhone(model.Phone))
            return InvalidSignup(model, "A valid name, CNIC, and phone number are required.");
        if (!ValidCoordinates(model.Latitude, model.Longitude))
            return InvalidSignup(model, "Please select your service location on the map before creating the account.");
        if (model.Age is < 16 or > 100 || model.Salary is < 0 or > 10000000)
            return InvalidSignup(model, "Enter a valid age and non-negative salary.");
        if (model.Gender != null && model.Gender is not ("Male" or "Female"))
            return InvalidSignup(model, "Choose a valid gender value.");
        if (await _context.Workers.AnyAsync(w => w.Cnic == model.Cnic))
            return InvalidSignup(model, "CNIC is already registered.");

        var habitIds = (model.HabitIds ?? new()).Where(h => h > 0).Distinct().ToList();
        var habits = await _context.Habits.Where(h => h.IsActive && h.IsWorkerSelectable && habitIds.Contains(h.HabitId)).ToListAsync();
        if (habits.Count == 0 || habits.Count != habitIds.Count)
            return InvalidSignup(model, "Select at least one valid, currently selectable habit.");
        if (HabitSelectionRules.FindExclusiveGroupConflict(habits) is { } conflict)
            return InvalidSignup(model, $"Only one habit from the '{conflict}' group may be selected.");

        var skills = ParseSkills(model.SkillsJson);
        if (skills.Count == 0) return InvalidSignup(model, "Please add at least one sub-skill to proceed.");
        if (!await SkillsAreValid(skills)) return InvalidSignup(model, "One or more selected skills do not belong to their category.");
        var primaryCategories = skills.Where(s => s.IsPrimary).Select(s => s.CategoryId).Distinct().ToList();
        if (primaryCategories.Count > 1) return InvalidSignup(model, "Only one primary category may be selected.");
        var primaryCategory = primaryCategories.SingleOrDefault();
        var experiences = ParseExperiences(model.ExperiencesJson, out var experienceError);
        if (experienceError != null) return InvalidSignup(model, experienceError);

        await using var transaction = await _context.Database.BeginTransactionAsync();
        var imagePath = await _images.SaveImageAsync(model.PictureFile);
        if (imagePath == "Invalid") return InvalidSignup(model, "Choose a valid JPG or PNG image up to 5 MB.");
        var worker = new Worker
        {
            Name = model.Name.Trim(),
            Cnic = model.Cnic,
            Phone = model.Phone!.Trim(),
            Address = model.Address?.Trim(),
            Password = _passwords.Hash(model.Password!),
            Age = model.Age,
            Salary = model.Salary,
            Gender = model.Gender ?? "Male",
            Bio = model.Bio?.Trim(),
            Latitude = (decimal)model.Latitude!.Value,
            Longitude = (decimal)model.Longitude!.Value,
            AvailableStatus = true,
            Picture = imagePath ?? "worker_default.jpg"
        };
        _context.Workers.Add(worker);
        await _context.SaveChangesAsync();
        foreach (var skill in skills)
            _context.WorkerCategories.Add(new WorkerCategory { WorkerId = worker.WorkerId, CategoryId = skill.CategoryId, SkillsId = skill.SkillsId, IsPrimary = primaryCategory == 0 ? skill == skills.First() : skill.CategoryId == primaryCategory });
        foreach (var exp in experiences)
            _context.Experiences.Add(new Experience { WorkerId = worker.WorkerId, WorkAt = exp.WorkAt, ExpDetail = exp.ExpDetail, Duration = exp.Duration });
        foreach (var habit in habits)
            _context.WorkerHabits.Add(new WorkerHabits { WorkerId = worker.WorkerId, HabitId = habit.HabitId, CreatedDate = DateTime.Now });
        await _context.SaveChangesAsync();
        await transaction.CommitAsync();
        TempData["Success"] = "Worker profile, skills, and habits created! Please log in.";
        return RedirectToAction("Login");
    }

    private async Task<IActionResult> SignupCompany(SignupViewModel model)
    {
        if (string.IsNullOrWhiteSpace(model.CompanyName) || !ValidEmail(model.Email) || string.IsNullOrWhiteSpace(model.LicenseNumber))
            return InvalidSignup(model, "Company name, a valid email, and license number are required.");
        if (model.Phone != null && !ValidPhone(model.Phone)) return InvalidSignup(model, "Enter a valid phone number.");
        if (await _context.Companies.AnyAsync(c => c.Email == model.Email || c.LicenseNumber == model.LicenseNumber))
            return InvalidSignup(model, "A company with this email or license number is already registered.");

        var imagePath = await _images.SaveImageAsync(model.PictureFile);
        if (imagePath == "Invalid") return InvalidSignup(model, "Choose a valid JPG or PNG image up to 5 MB.");
        _context.Companies.Add(new Company
        {
            CompanyName = model.CompanyName.Trim(),
            Email = model.Email!.Trim(),
            Password = _passwords.Hash(model.Password!),
            PhoneNo = model.Phone?.Trim() ?? "",
            LicenseNumber = model.LicenseNumber.Trim(),
            CompanyAddress = model.Address?.Trim() ?? "",
            CreatedAt = DateTime.Now,
            CompanyPicture = imagePath ?? "company_default.jpg"
        });
        await _context.SaveChangesAsync();
        TempData["Success"] = "Company registered successfully! Please log in.";
        return RedirectToAction("Login");
    }

    internal static bool ValidEmail(string? value) => !string.IsNullOrWhiteSpace(value) &&
        value.Length <= 100 && new EmailAddressAttribute().IsValid(value.Trim());
    internal static bool ValidPhone(string? value) => !string.IsNullOrWhiteSpace(value) &&
        value.Length <= 20 && Regex.IsMatch(value.Trim(), @"^[0-9+()\-\s]{7,20}$");
    internal static bool ValidCnic(string? value) => !string.IsNullOrWhiteSpace(value) &&
        Regex.IsMatch(value.Trim(), @"^(?:\d{13}|\d{5}-\d{7}-\d)$");
    internal static bool ValidCoordinates(double? lat, double? lng) => lat.HasValue && lng.HasValue &&
        double.IsFinite(lat.Value) && double.IsFinite(lng.Value) && lat.Value is >= -90 and <= 90 && lng.Value is >= -180 and <= 180;

    internal static List<SkillInput> ParseSkills(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new();
        try
        {
            return (JsonConvert.DeserializeObject<List<SkillInput>>(json) ?? new())
                .Where(s => s.CategoryId > 0 && s.SkillsId > 0)
                .GroupBy(s => (s.CategoryId, s.SkillsId)).Select(g => g.First()).ToList();
        }
        catch { return new(); }
    }

    internal async Task<bool> SkillsAreValid(List<SkillInput> skills)
    {
        var skillIds = skills.Select(s => s.SkillsId).Distinct().ToList();
        var valid = await _context.Skills.Where(s => skillIds.Contains(s.SkillsId))
            .Select(s => new { s.SkillsId, s.CategoryId }).ToListAsync();
        return skills.All(s => valid.Any(v => v.SkillsId == s.SkillsId && v.CategoryId == s.CategoryId));
    }

    internal static List<ExperienceInput> ParseExperiences(string? json, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(json)) return new();
        try
        {
            var rows = JsonConvert.DeserializeObject<List<ExperienceInput>>(json) ?? new();
            if (rows.Any(x => string.IsNullOrWhiteSpace(x.WorkAt) || x.WorkAt.Length > 150 ||
                              (x.ExpDetail?.Length ?? 0) > 4000 || (x.Duration?.Length ?? 0) > 50))
            {
                error = "Each experience needs a workplace and valid field lengths.";
                return new();
            }
            return rows;
        }
        catch { error = "Experience payload was not in the expected format."; return new(); }
    }

    private async Task PopulateSignupLookups()
    {
        ViewBag.Categories = await _context.Categories.OrderBy(c => c.CategoryName)
            .Select(c => new { c.CategoryId, c.CategoryName }).ToListAsync();
        ViewBag.Habits = await _context.Habits.Where(h => h.IsActive && h.IsWorkerSelectable)
            .OrderBy(h => h.SortOrder).ThenBy(h => h.Name)
            .Select(h => new { h.HabitId, h.Name, h.ExclusiveGroup }).ToListAsync();
    }

    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> AddSkills(string? returnTo)
    {
        ViewBag.Categories = await _context.Categories.OrderBy(c => c.CategoryName)
            .Select(c => new { c.CategoryId, c.CategoryName }).ToListAsync();
        ViewBag.ReturnTo = returnTo == "workerEdit" ? "workerEdit" : "signup";
        return View();
    }

    [HttpGet]
    public async Task<IActionResult> SkillsByCategory(int categoryId)
    {
        return Json(await _context.Skills.Where(s => s.CategoryId == categoryId)
            .Select(s => new { s.SkillsId, s.SkillName }).ToListAsync());
    }
}
