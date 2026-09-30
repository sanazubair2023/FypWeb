using System.Security.Claims;
using Fyp_Backend.Models;
using FypWeb.Helpers;
using FypWeb.ViewModels;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;

namespace FypWeb.Controllers
{
    public class AccountController : Controller
    {
        private readonly Fyp1Context _context;
        private readonly ImageService _images;

        public AccountController(Fyp1Context context, ImageService images)
        {
            _context = context;
            _images = images;
        }

        // ── LOGIN ────────────────────────────────────────────────────────────────
        [HttpGet]
        public IActionResult Login()
        {
            if (User?.Identity?.IsAuthenticated == true)
                return RedirectToAction("Index", "Home");
            return View(new LoginViewModel());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(LoginViewModel model)
        {
            var id = model.EmailOrCnic?.Trim() ?? "";
            var pwd = model.Password?.Trim() ?? "";

            if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(pwd))
            {
                TempData["Error"] = "Please enter your credentials.";
                return View(model);
            }

            ClaimsIdentity? identity = null;

            switch (model.Role)
            {
                case "Client":
                    var client = await _context.Clients.FirstOrDefaultAsync(c => c.Email == id);
                    if (client != null && client.Password == pwd) // plaintext compare (DB-compatible)
                        identity = BuildIdentity(client.ClientId, "Client", client.Name, client.Picture, client.Email);
                    break;

                case "Worker":
                    var worker = await _context.Workers.FirstOrDefaultAsync(w => w.Cnic == id);
                    if (worker != null && worker.Password == pwd)
                        identity = BuildIdentity(worker.WorkerId, "Worker", worker.Name ?? "", worker.Picture, "");
                    break;

                case "Company":
                    var company = await _context.Companies.FirstOrDefaultAsync(c => c.LicenseNumber == id);
                    if (company != null && company.Password == pwd)
                        identity = BuildIdentity(company.CompanyID, "Company", company.CompanyName, company.CompanyPicture, company.Email);
                    break;

                case "Police":
                    var police = await _context.PoliceOfficers.FirstOrDefaultAsync(p => p.BadgeID == id);
                    if (police != null && police.Password == pwd)
                        identity = BuildIdentity(police.PoliceID, "Police", police.StationName, "", police.Email);
                    break;
            }

            if (identity == null)
            {
                TempData["Error"] = "Invalid credentials.";
                return View(model);
            }

            var principal = new ClaimsPrincipal(identity);
            await HttpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                principal,
                new AuthenticationProperties { IsPersistent = true });

            return RedirectToAction("Index", "Home");
        }

        private static ClaimsIdentity BuildIdentity(int id, string role, string name, string? picture, string? email)
        {
            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, id.ToString()),
                new(ClaimTypes.Name, string.IsNullOrWhiteSpace(name) ? role : name),
                new(ClaimTypes.Role, role),
                new("Picture", picture ?? ""),
                new("Email", email ?? "")
            };
            return new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToAction("Login");
        }

        // ── SIGNUP ───────────────────────────────────────────────────────────────
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

            if (!string.IsNullOrEmpty(model.Password) && model.Password != model.ConfirmPassword)
            {
                TempData["Error"] = "Passwords do not match.";
                return View(model);
            }

            try
            {
                switch (model.Role)
                {
                    case "Client": return await SignupClient(model);
                    case "Worker": return await SignupWorker(model);
                    case "Company": return await SignupCompany(model);
                    default:
                        TempData["Error"] = "Invalid role. Self-registration is not available for this account type.";
                        return View(model);
                }
            }
            catch (Exception ex)
            {
                TempData["Error"] = "Error: " + (ex.InnerException?.Message ?? ex.Message);
                return View(model);
            }
        }

        private async Task<IActionResult> SignupClient(SignupViewModel model)
        {
            if (string.IsNullOrWhiteSpace(model.Email) || string.IsNullOrWhiteSpace(model.Name)
                || string.IsNullOrWhiteSpace(model.Phone) || string.IsNullOrWhiteSpace(model.Password))
            {
                TempData["Error"] = "Please fill out all required fields.";
                return View(model);
            }

            if (await _context.Clients.AnyAsync(c => c.Email == model.Email))
            {
                TempData["Error"] = "Email is already registered.";
                return View(model);
            }

            string safeFileName = model.Email!.Replace("@", "_").Replace(".", "_");
            string? imagePath = await _images.SaveImageAsync(model.PictureFile, safeFileName);
            if (imagePath == "Invalid")
            {
                TempData["Error"] = "Only .jpg, .jpeg, and .png files are allowed.";
                return View(model);
            }

            var client = new Client
            {
                Name = model.Name!,
                Email = model.Email!,
                Phone = model.Phone!,
                Password = model.Password!,
                Address = model.Address,
                Latitude = model.Latitude,
                Longitude = model.Longitude,
                Picture = imagePath
            };
            _context.Clients.Add(client);
            await _context.SaveChangesAsync();

            TempData["Success"] = "Client registered successfully! Please log in.";
            return RedirectToAction("Login");
        }

        private async Task<IActionResult> SignupWorker(SignupViewModel model)
        {
            if (string.IsNullOrWhiteSpace(model.Cnic) || string.IsNullOrWhiteSpace(model.Name)
                || string.IsNullOrWhiteSpace(model.Phone) || string.IsNullOrWhiteSpace(model.Password))
            {
                TempData["Error"] = "Please fill out all required worker fields.";
                return View(model);
            }

            if (await _context.Workers.AnyAsync(w => w.Cnic == model.Cnic))
            {
                TempData["Error"] = "CNIC is already registered.";
                return View(model);
            }

            var habitIds = (model.HabitIds ?? new List<int>()).Where(h => h > 0).Distinct().ToList();
            if (habitIds.Count == 0)
            {
                TempData["Error"] = "Please select at least one habit.";
                return View(model);
            }
            var validHabitIds = await _context.Habits.Where(h => habitIds.Contains(h.HabitId))
                .Select(h => h.HabitId).ToListAsync();

            using var transaction = await _context.Database.BeginTransactionAsync();

            string? imagePath = await _images.SaveImageAsync(model.PictureFile, model.Cnic!);
            if (imagePath == "Invalid")
            {
                TempData["Error"] = "Only .jpg, .jpeg, and .png files are allowed.";
                return View(model);
            }

            var worker = new Worker
            {
                Name = model.Name,
                Cnic = model.Cnic,
                Phone = model.Phone,
                Address = model.Address,
                Password = model.Password,
                Age = model.Age,
                Salary = model.Salary,
                Gender = model.Gender ?? "Male",
                Bio = model.Bio,
                Latitude = model.Latitude.HasValue ? (decimal?)model.Latitude.Value : null,
                Longitude = model.Longitude.HasValue ? (decimal?)model.Longitude.Value : null,
                AvailableStatus = true,
                Picture = imagePath ?? "worker_default.jpg"
            };
            _context.Workers.Add(worker);
            await _context.SaveChangesAsync();

            if (!string.IsNullOrWhiteSpace(model.ExperiencesJson))
            {
                var experiences = JsonConvert.DeserializeObject<List<ExperienceInput>>(model.ExperiencesJson);
                if (experiences != null)
                {
                    var uniqueJunctions = new HashSet<(int, int)>();
                    foreach (var exp in experiences)
                    {
                        _context.Experiences.Add(new Experience
                        {
                            WorkerId = worker.WorkerId,
                            WorkAt = exp.WorkAt,
                            ExpDetail = exp.ExpDetail,
                            Duration = exp.Duration
                        });

                        int catId = exp.CategoryId ?? 0;
                        int skillId = exp.SkillsId ?? 0;
                        if (catId > 0 && skillId > 0 && uniqueJunctions.Add((catId, skillId)))
                        {
                            _context.WorkerCategories.Add(new WorkerCategory
                            {
                                WorkerId = worker.WorkerId,
                                CategoryId = catId,
                                SkillsId = skillId
                            });
                        }
                    }
                    await _context.SaveChangesAsync();
                }
            }

            foreach (var hid in validHabitIds)
            {
                _context.WorkerHabits.Add(new WorkerHabits
                {
                    WorkerId = worker.WorkerId,
                    HabitId = hid,
                    CreatedDate = DateTime.Now
                });
            }
            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            TempData["Success"] = "Worker profile, skills and habits created! Please log in.";
            return RedirectToAction("Login");
        }

        private async Task<IActionResult> SignupCompany(SignupViewModel model)
        {
            if (string.IsNullOrWhiteSpace(model.CompanyName) || string.IsNullOrWhiteSpace(model.Email)
                || string.IsNullOrWhiteSpace(model.LicenseNumber) || string.IsNullOrWhiteSpace(model.Password))
            {
                TempData["Error"] = "Please fill out all company fields.";
                return View(model);
            }

            if (await _context.Companies.AnyAsync(c => c.Email == model.Email))
            {
                TempData["Error"] = "Email is already registered.";
                return View(model);
            }
            if (await _context.Companies.AnyAsync(c => c.LicenseNumber == model.LicenseNumber))
            {
                TempData["Error"] = "License number is already registered.";
                return View(model);
            }

            string safeFileName = "company_" + model.Email!.Replace("@", "_").Replace(".", "_");
            string? imagePath = await _images.SaveImageAsync(model.PictureFile, safeFileName);
            if (imagePath == "Invalid")
            {
                TempData["Error"] = "Only .jpg, .jpeg, and .png files are allowed.";
                return View(model);
            }

            var company = new Company
            {
                CompanyName = model.CompanyName!,
                Email = model.Email!,
                Password = model.Password!,
                PhoneNo = model.Phone ?? "",
                LicenseNumber = model.LicenseNumber!,
                CompanyAddress = model.Address ?? "",
                CreatedAt = DateTime.Now,
                CompanyPicture = imagePath ?? "company_default.jpg"
            };
            _context.Companies.Add(company);
            await _context.SaveChangesAsync();

            TempData["Success"] = "Company registered successfully! Please log in.";
            return RedirectToAction("Login");
        }

        private async Task PopulateSignupLookups()
        {
            ViewBag.Categories = await _context.Categories
                .Select(c => new { c.CategoryId, c.CategoryName }).ToListAsync();
            ViewBag.Habits = await _context.Habits
                .Where(h => h.IsActive)
                .OrderBy(h => h.SortOrder).ThenBy(h => h.Name)
                .Select(h => new { h.HabitId, h.Name }).ToListAsync();
        }

        // ── AJAX lookup used by the worker signup skills picker ───────────────────
        [HttpGet]
        public async Task<IActionResult> SkillsByCategory(int categoryId)
        {
            var skills = await _context.Skills
                .Where(s => s.CategoryId == categoryId)
                .Select(s => new { s.SkillsId, s.SkillName })
                .ToListAsync();
            return Json(skills);
        }
    }
}
