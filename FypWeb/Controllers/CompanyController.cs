using System.Security.Claims;
using Fyp_Backend.Models;
using FypWeb.Helpers;
using FypWeb.Services;
using FypWeb.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FypWeb.Controllers
{
    [Authorize(Roles = "Company")]
    public class CompanyController : Controller
    {
        private readonly Fyp1Context _context;
        private readonly ImageService _images;
        private readonly PasswordService _passwords;
        private readonly ILogger<CompanyController> _logger;
        public CompanyController(Fyp1Context context, ImageService images, PasswordService passwords, ILogger<CompanyController> logger)
        {
            _context = context;
            _images = images;
            _passwords = passwords;
            _logger = logger;
        }

        private int CompanyId => User.GetId();

        private static string CleanPic(string? raw)
        {
            raw = string.IsNullOrWhiteSpace(raw) ? "worker_default.jpg" : raw;
            if (raw.StartsWith("http")) return raw;
            if (raw.StartsWith("/Images/") || raw.StartsWith("Images/"))
                return raw.StartsWith("/") ? raw : "/" + raw;
            return $"/Images/{raw.TrimStart('/')}";
        }

        // ── DIRECTORY (all workers) ────────────────────────────────────────────────
        public async Task<IActionResult> Directory(string? search)
        {
            var company = await _context.Companies.FindAsync(CompanyId);
            ViewBag.CompanyName = company?.CompanyName ?? "Company";
            var workersQuery = _context.Workers.AsNoTracking().AsQueryable();
            if (!string.IsNullOrWhiteSpace(search)) workersQuery = workersQuery.Where(w => w.Name != null && w.Name.Contains(search.Trim()));
            var workers = await workersQuery.OrderBy(w => w.Name).Select(w => new
            { w.WorkerId, w.Name, w.Bio, w.Address, w.Picture, w.Salary }).ToListAsync();
            var ids = workers.Select(w => w.WorkerId).ToList();
            var categoryRows = await _context.WorkerCategories.Where(wc => ids.Contains(wc.WorkerId))
                .Join(_context.Categories, wc => wc.CategoryId, c => c.CategoryId,
                    (wc, c) => new { wc.WorkerId, wc.IsPrimary, c.CategoryName })
                .OrderByDescending(x => x.IsPrimary).ThenBy(x => x.CategoryName).ToListAsync();
            var ratingRows = await _context.Reviews.Where(r => r.ReviewerRole == "Client" && r.Interview != null &&
                r.Interview.WorkerId != null && ids.Contains(r.Interview.WorkerId.Value))
                .Select(r => new { WorkerId = r.Interview!.WorkerId!.Value, Rating = r.Rating ?? 0m }).ToListAsync();
            var result = workers.Select(w =>
            {
                var ratings = ratingRows.Where(r => r.WorkerId == w.WorkerId).Select(r => (double)r.Rating).ToList();
                return new CompanyWorkerVM
                {
                    WorkerId = w.WorkerId,
                    Name = w.Name ?? "N/A",
                    RoleTitle = w.Bio ?? "General Service Professional",
                    Location = PublicLocality(w.Address),
                    Picture = CleanPic(w.Picture),
                    Salary = w.Salary.HasValue ? $"Rs.{w.Salary.Value:F0}" : "Not set",
                    Rating = (ratings.Count > 0 ? Math.Round(ratings.Average(), 1) : 0).ToString("F1"),
                    Categories = categoryRows.Where(c => c.WorkerId == w.WorkerId && !string.IsNullOrEmpty(c.CategoryName))
                        .Select(c => c.CategoryName).Distinct().ToList()
                };
            }).ToList();
            ViewBag.Search = search;
            return View(result);
        }

        private static string PublicLocality(string? address)
        {
            if (string.IsNullOrWhiteSpace(address)) return "Location not specified";
            var parts = address.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return parts.Length > 1 ? parts[^1] : "Locality not specified";
        }

        // ── WORKER DETAIL + ISSUE CERTIFICATE ──────────────────────────────────────
        public async Task<IActionResult> WorkerDetail(int id)
        {
            var w = await _context.Workers.Where(x => x.WorkerId == id)
                .Select(x => new { x.WorkerId, x.Name, x.Cnic, x.Phone, x.Address, x.Picture }).FirstOrDefaultAsync();
            if (w == null) return NotFound();

            var catName = await _context.WorkerCategories.Where(wc => wc.WorkerId == id)
                .Join(_context.Categories, wc => wc.CategoryId, c => c.CategoryId, (wc, c) => c.CategoryName)
                .FirstOrDefaultAsync();

            var existing = await _context.WorkerCertifications
                .Where(wc => wc.WorkerID == id).OrderByDescending(wc => wc.IssuedDate)
                .Select(wc => new { wc.CertificateTitle, wc.TrainingEvaluationNotes, wc.IssuedDate })
                .FirstOrDefaultAsync();

            var vm = new WorkerCertificateVM
            {
                WorkerId = w.WorkerId,
                Name = w.Name ?? "N/A",
                Cnic = string.IsNullOrEmpty(w.Cnic) ? "Not provided" : w.Cnic,
                Phone = string.IsNullOrEmpty(w.Phone) ? "Not provided" : w.Phone,
                Address = string.IsNullOrEmpty(w.Address) ? "Not provided" : w.Address,
                Picture = CleanPic(w.Picture),
                RoleTitle = !string.IsNullOrWhiteSpace(catName) ? $"{catName!.Trim().ToUpper()} SPECIALIST" : "GENERAL SERVICE SPECIALIST",
                HasCertificate = existing != null,
                CertificateTitle = existing?.CertificateTitle,
                EvaluationNotes = existing?.TrainingEvaluationNotes,
                IssuedDate = existing != null ? existing.IssuedDate.ToString("dd-MM-yyyy") : null
            };
            return View(vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> IssueCertificate(int workerId, string certificateTitle, string? trainingEvaluationNotes)
        {
            if (string.IsNullOrWhiteSpace(certificateTitle) || certificateTitle.Trim().Length > 150 || trainingEvaluationNotes?.Length > 4000)
            {
                TempData["Error"] = "A certificate title of at most 150 characters is required; evaluation notes are limited to 4,000 characters.";
                return RedirectToAction("WorkerDetail", new { id = workerId });
            }
            if (!await _context.Workers.AnyAsync(w => w.WorkerId == workerId))
            { TempData["Error"] = "Worker not found."; return RedirectToAction("Directory"); }
            try
            {
                _context.WorkerCertifications.Add(new WorkerCertification
                {
                    WorkerID = workerId,
                    CompanyID = CompanyId,
                    CertificateTitle = certificateTitle.Trim(),
                    TrainingEvaluationNotes = trainingEvaluationNotes?.Trim() ?? "",
                    IssuedDate = DateTime.Now
                });
                await _context.SaveChangesAsync();
                TempData["Success"] = "Certificate issued and published successfully!";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Company {CompanyId} could not issue a legacy certificate to worker {WorkerId}", CompanyId, workerId);
                TempData["Error"] = "The certificate could not be issued. Please try again.";
            }
            return RedirectToAction("WorkerDetail", new { id = workerId });
        }

        // ── COMPANY DASHBOARD (RN CompanyDashboardScreen / GET api/company/dashboard) ──
        public async Task<IActionResult> Dashboard()
        {
            var company = await _context.Companies.AsNoTracking()
                .Where(c => c.CompanyID == CompanyId)
                .Select(c => new CompanyDashboardVM
                {
                    Id = c.CompanyID,
                    CompanyName = c.CompanyName,
                    LicenseNumber = c.LicenseNumber,
                    CompanyAddress = c.CompanyAddress,
                    Picture = c.CompanyPicture,
                    TotalCourses = _context.Courses.Count(course => course.CompanyID == c.CompanyID && !course.IsDeleted),
                    ActiveCourses = _context.Courses.Count(course => course.CompanyID == c.CompanyID && !course.IsDeleted && course.IsPublished && course.EndDate >= DateOnly.FromDateTime(DateTime.UtcNow)),
                    TotalEnrollments = _context.CourseEnrollments.Count(enrollment => enrollment.Course.CompanyID == c.CompanyID),
                    UniqueEnrolledWorkers = _context.CourseEnrollments
                        .Where(enrollment => enrollment.Course.CompanyID == c.CompanyID)
                        .Select(enrollment => enrollment.WorkerID)
                        .Distinct()
                        .Count(),
                    CompletedEnrollments = _context.CourseEnrollments.Count(enrollment => enrollment.Course.CompanyID == c.CompanyID && enrollment.Status == "Completed")
                }).FirstOrDefaultAsync();

            if (company == null) return NotFound();
            return View(company);
        }

        // ── COURSE MANAGEMENT LIST (RN CompanyCourseDashboardScreen) ───────────────
        // GET api/company/courses?scope=all|active|draft|past|archived
        public async Task<IActionResult> Courses(string scope = "active")
        {
            scope = scope?.Trim().ToLowerInvariant() ?? "active";
            if (scope is not ("all" or "active" or "draft" or "past" or "archived")) scope = "active";
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var query = _context.Courses.AsNoTracking().Where(x => x.CompanyID == CompanyId);
            query = scope switch
            {
                "active" => query.Where(x => !x.IsDeleted && x.IsPublished && x.EndDate >= today),
                "past" => query.Where(x => !x.IsDeleted && x.EndDate < today),
                "draft" => query.Where(x => !x.IsDeleted && !x.IsPublished),
                "archived" => query.Where(x => x.IsDeleted),
                _ => query.Where(x => !x.IsDeleted)
            };

            var rows = await query.OrderByDescending(x => x.CreatedAtUtc)
                .Select(x => new CourseCardVM
                {
                    CourseId = x.CourseID,
                    CourseName = x.CourseName,
                    StartDate = x.StartDate.ToString("dd MMM yyyy"),
                    EndDate = x.EndDate.ToString("dd MMM yyyy"),
                    ClassTimings = x.ClassTimings,
                    Description = x.Description,
                    IsPublished = x.IsPublished,
                    IsDeleted = x.IsDeleted,
                    EnrollmentCount = x.Enrollments.Count,
                    CompletedCount = x.Enrollments.Count(e => e.Status == "Completed")
                }).ToListAsync();

            ViewBag.Scope = scope;
            return View(rows);
        }

        // ── COURSE FORM (RN CompanyCourseFormScreen) ────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> CourseForm(int? id)
        {
            if (id == null || id == 0) return View(new CourseFormVM());

            var old = await _context.Courses.AsNoTracking()
                .FirstOrDefaultAsync(x => x.CourseID == id && x.CompanyID == CompanyId && !x.IsDeleted);
            if (old == null) return NotFound();

            // Split ClassTimings ("9:00 AM - 5:00 PM") back into the time fields.
            var times = old.ClassTimings.Split("-");

            return View(new CourseFormVM
            {
                CourseId = old.CourseID,
                CourseName = old.CourseName,
                StartDate = old.StartDate.ToString("yyyy-MM-dd"),
                EndDate = old.EndDate.ToString("yyyy-MM-dd"),
                StartTime = ParseTimeTo24h(times.Length > 0 ? times[0] : ""),
                EndTime = ParseTimeTo24h(times.Length > 1 ? times[1] : ""),
                Description = old.Description,
                Syllabus = old.Syllabus,
                IsPublished = old.IsPublished
            });
        }

        // "9:00 AM" / "05:00 PM" → "09:00" / "17:00" (for <input type="time">).
        private static string ParseTimeTo24h(string raw)
        {
            raw = (raw ?? "").Trim();
            if (DateTime.TryParse(raw, out var t)) return t.ToString("HH:mm");
            return raw;
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CourseForm(CourseFormVM model)
        {
            // Client-side rules from the RN form.
            bool datesOk = true, timesOk = true;
            if (string.IsNullOrWhiteSpace(model.CourseName) || model.CourseName.Trim().Length > 150)
                ModelState.AddModelError(string.Empty, "Course name is required and must be at most 150 characters.");
            if (model.Description?.Length > 4000 || model.Syllabus?.Length > 8000)
                ModelState.AddModelError(string.Empty, "Description is limited to 4,000 characters and syllabus to 8,000 characters.");

            // NOTE: each TryParse is evaluated on its own line — a short-circuited
            // "a || b" would leave end/et unassigned on later lines (CS0165).
            if (!DateOnly.TryParse(model.StartDate, out var start))
            { ModelState.AddModelError(string.Empty, "Please pick a valid start date."); datesOk = false; }
            if (!DateOnly.TryParse(model.EndDate, out var end))
            { ModelState.AddModelError(string.Empty, "Please pick a valid end date."); datesOk = false; }
            else if (datesOk && end < start)
            { ModelState.AddModelError(string.Empty, "End date cannot be before start date."); datesOk = false; }

            if (!TimeSpan.TryParse(model.StartTime, out var st))
            { ModelState.AddModelError(string.Empty, "Please pick a class start time."); timesOk = false; }
            if (!TimeSpan.TryParse(model.EndTime, out var et))
            { ModelState.AddModelError(string.Empty, "Please pick a class end time."); timesOk = false; }
            else if (timesOk && et <= st)
            { ModelState.AddModelError(string.Empty, "Class end time must be after start time."); timesOk = false; }

            if (timesOk && (st < TimeSpan.Zero || et < TimeSpan.Zero || st >= TimeSpan.FromDays(1) || et >= TimeSpan.FromDays(1)))
            { ModelState.AddModelError(string.Empty, "Class times must be valid times of day."); timesOk = false; }
            if (!datesOk || !timesOk || !ModelState.IsValid) return View(model);

            string classTimings =
                DateTime.Today.Add(st).ToString("h:mm tt") + " - " + DateTime.Today.Add(et).ToString("h:mm tt");

            if (model.CourseId == 0)
            {
                // POST api/company/courses
                _context.Courses.Add(new Course
                {
                    CompanyID = CompanyId,
                    CourseName = model.CourseName.Trim(),
                    StartDate = start,
                    EndDate = end,
                    ClassTimings = classTimings,
                    Description = model.Description?.Trim(),
                    Syllabus = model.Syllabus?.Trim(),
                    IsPublished = model.IsPublished,
                    CreatedAtUtc = DateTime.UtcNow
                });
                await _context.SaveChangesAsync();
                TempData["Success"] = "Course created.";
            }
            else
            {
                // PUT api/company/courses/{id}
                var item = await _context.Courses.FirstOrDefaultAsync(
                    x => x.CourseID == model.CourseId && x.CompanyID == CompanyId && !x.IsDeleted);
                if (item == null) return NotFound();

                item.CourseName = model.CourseName.Trim();
                item.StartDate = start;
                item.EndDate = end;
                item.ClassTimings = classTimings;
                item.Description = model.Description?.Trim();
                item.Syllabus = model.Syllabus?.Trim();
                item.IsPublished = model.IsPublished;
                item.UpdatedAtUtc = DateTime.UtcNow;
                await _context.SaveChangesAsync();
                TempData["Success"] = "Course updated.";
            }

            return RedirectToAction("Courses");
        }

        // ── COURSE DETAIL + ENROLLMENTS (RN CompanyCourseDetailScreen) ─────────────
        public async Task<IActionResult> CourseDetail(int id)
        {
            var item = await _context.Courses.AsNoTracking()
                .Where(x => x.CourseID == id && x.CompanyID == CompanyId)
                .Select(x => new CourseDetailVM
                {
                    CourseId = x.CourseID,
                    CourseName = x.CourseName,
                    StartDate = x.StartDate.ToString("dd MMM yyyy"),
                    EndDate = x.EndDate.ToString("dd MMM yyyy"),
                    ClassTimings = x.ClassTimings,
                    Description = x.Description,
                    Syllabus = x.Syllabus,
                    IsPublished = x.IsPublished,
                    IsDeleted = x.IsDeleted,
                    Enrollments = x.Enrollments.OrderByDescending(e => e.EnrolledAtUtc).Select(e => new EnrollmentRowVM
                    {
                        EnrollmentId = e.EnrollmentID,
                        WorkerId = e.WorkerID,
                        WorkerName = e.Worker.Name ?? "Worker",
                        WorkerPicture = e.Worker.Picture,
                        Status = e.Status,
                        EnrolledAt = e.EnrolledAtUtc.ToString("dd MMM yyyy"),
                        CertificateId = e.Certificate == null ? (int?)null : e.Certificate.CertificateID,
                        CertificateCode = e.Certificate == null ? null : e.Certificate.CertificateCode
                    }).ToList()
                }).FirstOrDefaultAsync();

            if (item == null) return NotFound();
            return View(item);
        }

        // POST api/company/enrollments/{id}/complete — issues the certificate.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CompleteEnrollment(int enrollmentId)
        {
            await using var tx = await _context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
            var enrollment = await _context.CourseEnrollments
                .Include(x => x.Course).Include(x => x.Certificate)
                .FirstOrDefaultAsync(x => x.EnrollmentID == enrollmentId && x.Course.CompanyID == CompanyId);
            if (enrollment == null) { TempData["Error"] = "Enrollment not found."; return RedirectToAction("Courses"); }
            if (enrollment.Course.IsDeleted)
            {
                TempData["Error"] = "An archived course is read-only; restore it before completing enrollments.";
                return RedirectToAction("CourseDetail", new { id = enrollment.CourseID });
            }
            if (enrollment.Status == "Withdrawn")
            {
                TempData["Error"] = "A withdrawn enrollment cannot be completed.";
                return RedirectToAction("CourseDetail", new { id = enrollment.CourseID });
            }
            if (enrollment.Certificate != null)
            {
                TempData["Success"] = "Already completed.";
                return RedirectToAction("CourseDetail", new { id = enrollment.CourseID });
            }

            enrollment.Status = "Completed";
            enrollment.CompletionDateUtc = DateTime.UtcNow;
            enrollment.UpdatedAtUtc = DateTime.UtcNow;

            // Unique readable code, same format as the API.
            string code;
            using var rng = System.Security.Cryptography.RandomNumberGenerator.Create();
            do
            {
                var bytes = new byte[6];
                rng.GetBytes(bytes);
                code = "FYP-COURSE-" + Convert.ToHexString(bytes);
            } while (await _context.CourseCertificates.AnyAsync(x => x.CertificateCode == code));

            var certificate = new CourseCertificate
            {
                EnrollmentID = enrollment.EnrollmentID,
                CourseID = enrollment.CourseID,
                WorkerID = enrollment.WorkerID,
                CompanyID = CompanyId,
                CertificateCode = code,
                IssuedDateUtc = DateTime.UtcNow,
                CreatedAtUtc = DateTime.UtcNow
            };
            _context.CourseCertificates.Add(certificate);
            try
            {
                await _context.SaveChangesAsync();
                await tx.CommitAsync();
            }
            catch (DbUpdateException ex)
            {
                await tx.RollbackAsync();
                _logger.LogWarning(ex, "Concurrent completion detected for enrollment {EnrollmentId}", enrollmentId);
                TempData["Error"] = "Completion was already processed or conflicts with an existing certificate. Refresh the course and try again.";
                return RedirectToAction("CourseDetail", new { id = enrollment.CourseID });
            }

            TempData["Success"] = "Worker completed the course and certificate " + code + " was issued.";
            return RedirectToAction("CourseDetail", new { id = enrollment.CourseID });
        }

        // DELETE api/company/courses/{id} — hard delete when no enrollments,
        // otherwise soft-delete (archive) to preserve history.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteCourse(int id)
        {
            var item = await _context.Courses.Include(x => x.Enrollments)
                .FirstOrDefaultAsync(x => x.CourseID == id && x.CompanyID == CompanyId);
            if (item == null) { TempData["Error"] = "Course not found."; return RedirectToAction("Courses"); }

            if (item.IsDeleted)
            {
                TempData["Error"] = "This course is already archived.";
                return RedirectToAction("Courses", new { scope = "archived" });
            }
            if (item.Enrollments.Count == 0)
            {
                _context.Courses.Remove(item);
                await _context.SaveChangesAsync();
                TempData["Success"] = "Course permanently deleted.";
            }
            else
            {
                item.IsDeleted = true;
                item.IsPublished = false;
                item.DeletedAtUtc = DateTime.UtcNow;
                item.UpdatedAtUtc = DateTime.UtcNow;
                await _context.SaveChangesAsync();
                TempData["Success"] = "Course archived because it has enrollment history.";
            }
            return RedirectToAction("Courses");
        }

        // ── COMPANY EDIT PROFILE (RN Signup isEdit Company → UpdateCompany) ────────
        [HttpGet]
        public async Task<IActionResult> EditProfile()
        {
            var company = await _context.Companies.FindAsync(CompanyId);
            if (company == null) return NotFound();

            return View(new CompanyEditProfileVM
            {
                CompanyName = company.CompanyName,
                Email = company.Email,
                Phone = company.PhoneNo,
                Address = company.CompanyAddress,
                LicenseNumber = company.LicenseNumber,
                CurrentPicture = company.CompanyPicture
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditProfile(CompanyEditProfileVM model)
        {
            if (string.IsNullOrWhiteSpace(model.CompanyName) || model.CompanyName.Trim().Length > 150 ||
                !AccountController.ValidEmail(model.Email) || !AccountController.ValidPhone(model.Phone) ||
                string.IsNullOrWhiteSpace(model.LicenseNumber) || model.LicenseNumber.Trim().Length > 50 ||
                string.IsNullOrWhiteSpace(model.Address) || model.Address.Trim().Length > 255)
                ModelState.AddModelError(string.Empty, "Enter valid company, email, phone, license, and address details within their field limits.");
            if (!string.IsNullOrEmpty(model.Password) && model.Password != "********" &&
                (!PasswordService.IsValidNewPassword(model.Password) || model.Password != model.ConfirmPassword))
                ModelState.AddModelError(string.Empty, "New passwords must match and contain 8–100 characters.");
            var existing = await _context.Companies.FindAsync(CompanyId);
            if (existing == null) return NotFound();
            var email = model.Email?.Trim().ToLowerInvariant() ?? "";
            var license = model.LicenseNumber?.Trim() ?? "";
            if (await _context.Companies.AnyAsync(c => c.CompanyID != CompanyId && c.Email.ToLower() == email))
                ModelState.AddModelError(nameof(model.Email), "That email is already registered.");
            if (await _context.Companies.AnyAsync(c => c.CompanyID != CompanyId && c.LicenseNumber == license))
                ModelState.AddModelError(nameof(model.LicenseNumber), "That license number is already registered.");
            if (!ModelState.IsValid) { model.CurrentPicture = existing.CompanyPicture; return View(model); }
            try
            {
                if (model.LogoFile != null)
                {
                    var imagePath = await _images.SaveImageAsync(model.LogoFile);
                    if (imagePath == "Invalid")
                    { ModelState.AddModelError(string.Empty, "Choose a valid JPG or PNG image up to 5 MB."); model.CurrentPicture = existing.CompanyPicture; return View(model); }
                    if (imagePath != null) existing.CompanyPicture = imagePath;
                }
                existing.CompanyName = model.CompanyName!.Trim(); existing.Email = email; existing.PhoneNo = model.Phone!.Trim();
                existing.CompanyAddress = model.Address!.Trim(); existing.LicenseNumber = license;
                if (!string.IsNullOrEmpty(model.Password) && model.Password != "********") existing.Password = _passwords.Hash(model.Password!);
                await _context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Company {CompanyId} profile update failed", CompanyId);
                ModelState.AddModelError(string.Empty, "Profile update could not be completed. Please try again.");
                model.CurrentPicture = existing.CompanyPicture; return View(model);
            }
            await RefreshSignInAsync(existing.CompanyID, existing.CompanyName, existing.CompanyPicture, existing.Email, existing.Password);
            TempData["Success"] = "Profile updated successfully!";
            return RedirectToAction("Dashboard");
        }

        // Re-issues the auth cookie after a profile edit (same claims as Login).
        private async Task RefreshSignInAsync(int companyId, string name, string? picture, string email, string? storedPassword)
        {
            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, companyId.ToString()),
                new(ClaimTypes.Name, string.IsNullOrWhiteSpace(name) ? "Company" : name),
                new(ClaimTypes.Role, "Company"),
                new("Picture", picture ?? ""),
                new("Email", email ?? ""),
                new("PasswordStamp", PasswordService.Stamp(storedPassword))
            };
            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            await HttpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                new ClaimsPrincipal(identity),
                new AuthenticationProperties { IsPersistent = true });
        }
    }
}
