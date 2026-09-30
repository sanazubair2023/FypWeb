using Fyp_Backend.Models;
using FypWeb.Helpers;
using FypWeb.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FypWeb.Controllers
{
    [Authorize(Roles = "Company")]
    public class CompanyController : Controller
    {
        private readonly Fyp1Context _context;
        public CompanyController(Fyp1Context context) => _context = context;

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
    }
}
