using System.Security.Claims;
using Fyp_Backend.Models;
using FypWeb.Helpers;
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
        public CompanyController(Fyp1Context context, ImageService images)
        {
            _context = context;
            _images = images;
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

            var workerList = await _context.Workers
                .Select(w => new { w.WorkerId, w.Name, w.Bio, w.Address, w.Picture, w.Salary })
                .ToListAsync();

            var categoryMap = await _context.WorkerCategories
                .Join(_context.Categories, wc => wc.CategoryId, c => c.CategoryId, (wc, c) => new { wc.WorkerId, c.CategoryName })
                .ToListAsync();

            var result = workerList.Select(w => new CompanyWorkerVM
            {
                WorkerId = w.WorkerId,
                Name = w.Name ?? "N/A",
                RoleTitle = w.Bio ?? "General Service Professional",
                Location = w.Address ?? "Rawalpindi",
                Picture = CleanPic(w.Picture),
                Salary = w.Salary.HasValue ? "Rs." + w.Salary.Value.ToString("F0") : "Rs.25000",
                Rating = "4.0",
                Categories = categoryMap.Where(cm => cm.WorkerId == w.WorkerId).Select(cm => cm.CategoryName).Distinct().ToList()
            }).ToList();

            if (!string.IsNullOrWhiteSpace(search))
                result = result.Where(r => r.Name.Contains(search, StringComparison.OrdinalIgnoreCase)).ToList();

            ViewBag.Search = search;
            return View(result);
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
                Cnic = string.IsNullOrEmpty(w.Cnic) ? "37405-XXXXXXX-X" : w.Cnic,
                Phone = string.IsNullOrEmpty(w.Phone) ? "+92 300 555 1234" : w.Phone,
                Address = string.IsNullOrEmpty(w.Address) ? "Rawalpindi, PK" : w.Address,
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
            if (string.IsNullOrWhiteSpace(certificateTitle))
            {
                TempData["Error"] = "Certificate title is required.";
                return RedirectToAction("WorkerDetail", new { id = workerId });
            }

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
        // GET api/company/courses?scope=active|past|archived
        public async Task<IActionResult> Courses(string scope = "active")
        {
            scope = scope?.ToLowerInvariant() ?? "active";
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var query = _context.Courses.AsNoTracking().Where(x => x.CompanyID == CompanyId);
            query = scope switch
            {
                "active" => query.Where(x => !x.IsDeleted && x.IsPublished && x.EndDate >= today),
                "past" => query.Where(x => !x.IsDeleted && x.EndDate < today),
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
            if (string.IsNullOrWhiteSpace(model.CourseName))
                ModelState.AddModelError(string.Empty, "Course name is required.");

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
            await using var tx = await _context.Database.BeginTransactionAsync();
            var enrollment = await _context.CourseEnrollments
                .Include(x => x.Course).Include(x => x.Certificate)
                .FirstOrDefaultAsync(x => x.EnrollmentID == enrollmentId && x.Course.CompanyID == CompanyId);
            if (enrollment == null) { TempData["Error"] = "Enrollment not found."; return RedirectToAction("Courses"); }
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
            var rng = System.Security.Cryptography.RandomNumberGenerator.Create();
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
            await _context.SaveChangesAsync();
            await tx.CommitAsync();

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
            if (string.IsNullOrWhiteSpace(model.CompanyName) || string.IsNullOrWhiteSpace(model.Email)
                || string.IsNullOrWhiteSpace(model.Phone) || string.IsNullOrWhiteSpace(model.LicenseNumber)
                || string.IsNullOrWhiteSpace(model.Address))
            {
                ModelState.AddModelError(string.Empty, "Please fill out all company details.");
            }
            if (!string.IsNullOrEmpty(model.Password) && model.Password != model.ConfirmPassword)
            {
                ModelState.AddModelError(string.Empty, "Passwords do not match.");
            }

            var existing = await _context.Companies.FindAsync(CompanyId);
            if (existing == null) return NotFound();

            if (!ModelState.IsValid)
            {
                model.CurrentPicture = existing.CompanyPicture;
                return View(model);
            }

            try
            {
                if (model.LogoFile != null)
                {
                    string safeFileName = "company_" + model.Email!.Replace("@", "_").Replace(".", "_");
                    string? imagePath = await _images.SaveImageAsync(model.LogoFile, safeFileName);
                    if (imagePath == "Invalid")
                    {
                        ModelState.AddModelError(string.Empty, "Only .jpg, .jpeg, and .png files are allowed.");
                        model.CurrentPicture = existing.CompanyPicture;
                        return View(model);
                    }
                    if (imagePath != null) existing.CompanyPicture = imagePath;
                }

                existing.CompanyName = model.CompanyName!;
                existing.Email = model.Email!;
                existing.PhoneNo = model.Phone!;
                existing.CompanyAddress = model.Address!;
                existing.LicenseNumber = model.LicenseNumber!;

                if (!string.IsNullOrEmpty(model.Password) && model.Password != "********")
                {
                    existing.Password = model.Password;
                }

                await _context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                ModelState.AddModelError(string.Empty, "Update failed: " + (ex.InnerException?.Message ?? ex.Message));
                model.CurrentPicture = existing.CompanyPicture;
                return View(model);
            }

            await RefreshSignInAsync(existing.CompanyID, existing.CompanyName, existing.CompanyPicture, existing.Email);

            TempData["Success"] = "Profile updated successfully!";
            return RedirectToAction("Dashboard");
        }

        // Re-issues the auth cookie after a profile edit (same claims as Login).
        private async Task RefreshSignInAsync(int companyId, string name, string? picture, string email)
        {
            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, companyId.ToString()),
                new(ClaimTypes.Name, string.IsNullOrWhiteSpace(name) ? "Company" : name),
                new(ClaimTypes.Role, "Company"),
                new("Picture", picture ?? ""),
                new("Email", email ?? "")
            };
            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            await HttpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                new ClaimsPrincipal(identity),
                new AuthenticationProperties { IsPersistent = true });
        }
    }
}
