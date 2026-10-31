using Fyp_Backend.Models;
using FypWeb.Helpers;
using FypWeb.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FypWeb.Controllers
{
    [Authorize(Roles = "Police")]
    public class PoliceController : Controller
    {
        private readonly Fyp1Context _context;
        private const int CERT_VALIDITY_YEARS = 5;
        public PoliceController(Fyp1Context context) => _context = context;

        private int PoliceId => User.GetId();
        private static bool IsValid(PoliceRecords? c) => c != null && !c.IsRevoked && c.ExpiryDate > DateTime.Now;

        private static string CleanPic(string? raw)
        {
            raw = string.IsNullOrWhiteSpace(raw) ? "worker_default.jpg" : raw;
            if (raw.StartsWith("http")) return raw;
            if (raw.StartsWith("/Images/") || raw.StartsWith("Images/"))
                return raw.StartsWith("/") ? raw : "/" + raw;
            return $"/Images/{raw.TrimStart('/')}";
        }

        // ── PORTAL (all workers + certification status) ─────────────────────────────
        public async Task<IActionResult> Portal(string? searchCnic)
        {
            var query = _context.Workers.AsQueryable();
            if (!string.IsNullOrWhiteSpace(searchCnic))
                query = query.Where(w => w.Cnic != null && w.Cnic.Contains(searchCnic));

            var workers = await query.Select(w => new
            {
                w.WorkerId,
                Name = w.Name ?? "",
                Cnic = w.Cnic ?? "",
                w.Picture,
                Category = _context.WorkerCategories.Where(wc => wc.WorkerId == w.WorkerId)
                    .Join(_context.Categories, wc => wc.CategoryId, c => c.CategoryId, (wc, c) => c.CategoryName)
                    .FirstOrDefault(),
                Cert = _context.PoliceRecords.Where(pr => pr.WorkerID == w.WorkerId && !pr.IsRevoked)
                    .OrderByDescending(pr => pr.IssuedDate)
                    .Select(pr => new { pr.ExpiryDate }).FirstOrDefault()
            }).ToListAsync();

            var list = workers.Select(w =>
            {
                bool isCertified = w.Cert != null && w.Cert.ExpiryDate > DateTime.Now;
                return new PoliceWorkerVM
                {
                    Id = w.WorkerId,
                    Name = w.Name,
                    Category = string.IsNullOrWhiteSpace(w.Category) ? "Domestic Worker" : w.Category!,
                    Cnic = w.Cnic,
                    Picture = CleanPic(w.Picture),
                    IsCertified = isCertified,
                    CertExpiry = w.Cert != null ? w.Cert.ExpiryDate.ToString("dd-MM-yyyy") : null
                };
            }).ToList();

            ViewBag.TotalResults = list.Count;
            ViewBag.CertifiedCount = list.Count(x => x.IsCertified);
            ViewBag.UncertifiedCount = list.Count(x => !x.IsCertified);
            ViewBag.SearchCnic = searchCnic;
            return View(list);
        }

        // ── WORKER DETAIL + ISSUE/RENEW CHARACTER CERTIFICATE ──────────────────────
        public async Task<IActionResult> WorkerDetail(int id)
        {
            var worker = await _context.Workers.Where(w => w.WorkerId == id)
                .Select(w => new { w.WorkerId, Name = w.Name ?? "", Cnic = w.Cnic ?? "", w.Picture }).FirstOrDefaultAsync();
            if (worker == null) return NotFound();

            var cert = await _context.PoliceRecords
                .Where(pr => pr.WorkerID == id && !pr.IsRevoked)
                .OrderByDescending(pr => pr.IssuedDate).FirstOrDefaultAsync();

            var vm = new CharacterCertificateVM
            {
                WorkerId = worker.WorkerId,
                Name = worker.Name,
                Cnic = worker.Cnic,
                Picture = CleanPic(worker.Picture),
                HasValidCertificate = IsValid(cert),
                Status = IsValid(cert) ? "Verified" : (cert != null ? "Expired" : "NotIssued"),
                CertificateNo = cert?.CertificateNo,
                CharacterStatus = cert?.CharacterStatus,
                Remarks = cert?.Remarks,
                IssuingStation = cert?.IssuingStation,
                IssuingBadge = cert?.IssuingBadge,
                IssuedDate = cert != null ? cert.IssuedDate.ToString("dd-MM-yyyy") : null,
                ExpiryDate = cert != null ? cert.ExpiryDate.ToString("dd-MM-yyyy") : null
            };
            return View(vm);
        }

        private static string CnicDigits(string? value) => new((value ?? "").Where(char.IsDigit).ToArray());

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> IssueCertificate(int workerId, string characterStatus, string? certificateNo,
            string? remarks, string? verifiedCnic, bool cnicVerified)
        {
            if (workerId <= 0) { TempData["Error"] = "A valid Worker ID is required."; return RedirectToAction("Portal"); }
            if (string.IsNullOrWhiteSpace(characterStatus) || characterStatus.Trim().Length > 50 || certificateNo?.Trim().Length > 50 || remarks?.Length > 4000)
            { TempData["Error"] = "Character status and certificate number are limited to 50 characters; remarks to 4,000."; return RedirectToAction("WorkerDetail", new { id = workerId }); }
            if (!cnicVerified)
            { TempData["Error"] = "Please confirm you have verified the worker's CNIC."; return RedirectToAction("WorkerDetail", new { id = workerId }); }

            var worker = await _context.Workers.FindAsync(workerId);
            if (worker == null) { TempData["Error"] = "Worker does not exist."; return RedirectToAction("Portal"); }

            if (CnicDigits(verifiedCnic).Length != 13 || CnicDigits(verifiedCnic) != CnicDigits(worker.Cnic))
            { TempData["Error"] = "The entered CNIC does not match this worker's registered CNIC."; return RedirectToAction("WorkerDetail", new { id = workerId }); }
            var officer = await _context.PoliceOfficers.FindAsync(PoliceId);
            if (officer == null) { TempData["Error"] = "Your police session is invalid. Please log in again."; return RedirectToAction("Portal"); }

            await using var transaction = await _context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
            var now = DateTime.Now;
            var expiry = now.AddYears(CERT_VALIDITY_YEARS);
            string certNo = string.IsNullOrWhiteSpace(certificateNo)
                ? $"CC-{worker.WorkerId}-{now:yyyyMMdd}" : certificateNo.Trim();

            var cert = await _context.PoliceRecords.Where(pr => pr.WorkerID == workerId).OrderByDescending(pr => pr.RecordID).FirstOrDefaultAsync();
            bool isRenewal = cert != null;
            cert ??= new PoliceRecords { WorkerID = workerId };

            cert.PoliceID = officer.PoliceID;
            cert.CertificateNo = certNo;
            cert.CharacterStatus = characterStatus.Trim();
            cert.Remarks = remarks?.Trim() ?? string.Empty;
            cert.VerifiedCnic = worker.Cnic;
            cert.CnicVerified = cnicVerified;
            cert.IssuingStation = officer.StationName;
            cert.IssuingBadge = officer.BadgeID;
            cert.IssuedDate = now;
            cert.ExpiryDate = expiry;
            cert.IsRevoked = false;

            if (!isRenewal) _context.PoliceRecords.Add(cert);
            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            TempData["Success"] = isRenewal
                ? $"Character certificate renewed successfully. Valid until {expiry:dd-MM-yyyy}."
                : $"Character certificate issued successfully. Valid until {expiry:dd-MM-yyyy}.";
            return RedirectToAction("WorkerDetail", new { id = workerId });
        }
    }
}
