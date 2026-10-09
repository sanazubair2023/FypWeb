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

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View();
        }
    }
}
