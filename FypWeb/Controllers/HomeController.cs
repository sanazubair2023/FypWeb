using Fyp_Backend.Models;
using FypWeb.Helpers;
using FypWeb.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FypWeb.Controllers
{
    public class HomeController : Controller
    {
        private readonly Fyp1Context _context;
        public HomeController(Fyp1Context context) => _context = context;

        public IActionResult Index()
        {
            if (User?.Identity?.IsAuthenticated == true)
            {
                return User.GetRole() switch
                {
                    "Client" => RedirectToAction("Dashboard", "Client"),
                    "Worker" => RedirectToAction("Dashboard", "Worker"),
                    "Company" => RedirectToAction("Dashboard", "Company"),
                    "Police" => RedirectToAction("Portal", "Police"),
                    _ => RedirectToAction("Login", "Account")
                };
            }
            return RedirectToAction("Login", "Account");
        }

        // ── PUBLIC COURSE-CERTIFICATE VERIFIER (api/course-certificates/verify) ──
        // Anyone can paste a certificate code (e.g. FYP-COURSE-…) and see
        // whether it is valid — no login required, exactly like the API.
        [HttpGet]
        [AllowAnonymous]
        public async Task<IActionResult> VerifyCertificate(string? code)
        {
            var model = new CertificateVM();
            if (!string.IsNullOrWhiteSpace(code))
            {
                code = code.Trim();
                model = await _context.CourseCertificates.AsNoTracking()
                    .Where(c => c.CertificateCode == code)
                    .Select(c => new CertificateVM
                    {
                        CertificateId = c.CertificateID,
                        CertificateCode = c.CertificateCode,
                        WorkerName = c.Worker.Name ?? "Worker",
                        CompanyName = c.Company.CompanyName,
                        CourseName = c.Course.CourseName,
                        IssuedDate = c.IssuedDateUtc.ToString("dd MMM yyyy"),
                        IsValid = !c.IsRevoked
                    }).FirstOrDefaultAsync() ?? new CertificateVM { CertificateCode = code };

                ViewBag.Searched = true;
            }
            return View(model);
        }

        // ── PUBLIC CLIENT PROFILE (RN ClientProfileScreen → api GetClientDetail) ──
        // Read-only profile reachable by any authenticated user, exactly like the
        // API: clients arrive from worker-detail review names, workers from their
        // request cards. Contact details are never included (the RN screen hides
        // them even though the API returns them).
        [Authorize]
        [HttpGet]
        public async Task<IActionResult> ClientProfile(int id)
        {
            var client = await _context.Clients.AsNoTracking()
                .FirstOrDefaultAsync(c => c.ClientId == id);
            if (client == null) return NotFound();

            // Reviews left BY workers FOR this client, newest first
            // (ordering on the real columns, matching the API).
            var reviewRows = await _context.Reviews.AsNoTracking()
                .Where(r => r.Interview != null && r.Interview.ClientId == id && r.ReviewerRole == "Worker")
                .OrderByDescending(r => r.ReviewDate).ThenByDescending(r => r.ReviewId)
                .Select(r => new
                {
                    r.InterviewId,
                    Rating = r.Rating ?? 0m,
                    r.Comment,
                    r.ReviewDate,
                    ReviewerName = r.Interview!.Worker != null ? r.Interview.Worker.Name : "Anonymous Worker",
                    ReviewerPicture = r.Interview.Worker != null ? r.Interview.Worker.Picture : null
                }).ToListAsync();

            // Worked period on every review: Hiring_Date -> Termination / Resignation date.
            var windows = await ReviewHelpers.LoadContractWindowsAsync(_context,
                reviewRows.Where(x => x.InterviewId.HasValue).Select(x => x.InterviewId!.Value).Distinct().ToList(),
                reviewRows.Where(x => x.InterviewId.HasValue)
                    .GroupBy(x => x.InterviewId!.Value)
                    .ToDictionary(g => g.Key, g => g.Max(x => x.ReviewDate)));

            var vm = new ClientProfileVM
            {
                ClientId = client.ClientId,
                Name = client.Name ?? "Client",
                Picture = client.Picture,
                Address = client.Address,
                ReviewCount = reviewRows.Count,
                Rating = reviewRows.Any() ? Math.Round(reviewRows.Average(r => (double)r.Rating), 1) : 0.0,
                Reviews = reviewRows.Select(r =>
                {
                    windows.TryGetValue(r.InterviewId ?? 0, out var w);
                    return new ClientProfileReviewVM
                    {
                        ReviewerName = r.ReviewerName ?? "Anonymous Worker",
                        ReviewerPicture = r.ReviewerPicture,
                        Rating = r.Rating,
                        Comment = r.Comment,
                        Date = r.ReviewDate != null ? r.ReviewDate.Value.ToString("dd MMM yyyy") : "N/A",
                        WorkedPeriod = ReviewHelpers.FormatWorkedPeriod(w.From, w.To)
                    };
                }).ToList()
            };
            return View("~/Views/Client/Profile.cshtml", vm);
            
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View();
        }
    }
}
