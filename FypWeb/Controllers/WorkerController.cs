using Fyp_Backend.Models;
using FypWeb.Helpers;
using FypWeb.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FypWeb.Controllers
{
    [Authorize(Roles = "Worker")]
    public class WorkerController : Controller
    {
        private readonly Fyp1Context _context;
        public WorkerController(Fyp1Context context) => _context = context;

        private int WorkerId => User.GetId();

        // ── DASHBOARD ─────────────────────────────────────────────────────────────
        public async Task<IActionResult> Dashboard()
        {
            int workerId = WorkerId;
            var worker = await _context.Workers.Include(w => w.Experiences)
                .FirstOrDefaultAsync(w => w.WorkerId == workerId);
            if (worker == null) return NotFound();

            var catNames = await _context.WorkerCategories
                .Where(wc => wc.WorkerId == workerId)
                .Join(_context.Categories, wc => wc.CategoryId, c => c.CategoryId, (wc, c) => c.CategoryName)
                .Where(n => !string.IsNullOrEmpty(n)).Distinct().ToListAsync();

            var slots = await _context.WorkerTimeSlots.Where(s => s.WorkerId == workerId)
                .OrderBy(s => s.StartTime)
                .Select(s => new SlotVM { Id = s.Id, StartTime = s.StartTime.ToString(@"hh\:mm"), EndTime = s.EndTime.ToString(@"hh\:mm") })
                .ToListAsync();

            var reviews = await _context.Reviews
                .Where(r => r.Interview != null && r.Interview.WorkerId == workerId)
                .OrderByDescending(r => r.ReviewDate)
                .Select(r => new ReviewVM
                {
                    Rating = r.Rating ?? 0,
                    Comment = r.Comment,
                    ReviewerRole = r.ReviewerRole ?? "Client",
                    ReviewDate = r.ReviewDate != null ? r.ReviewDate.Value.ToString("dd MMM yyyy") : ""
                }).ToListAsync();

            var habits = await _context.WorkerHabits
                .Where(wh => wh.WorkerId == workerId && wh.Habit != null)
                .Select(wh => wh.Habit!.Name).ToListAsync();

            var vm = new WorkerDashboardVM
            {
                Id = worker.WorkerId,
                Name = worker.Name ?? "",
                Cnic = worker.Cnic,
                Phone = worker.Phone,
                Address = worker.Address,
                Picture = worker.Picture,
                Gender = worker.Gender,
                Age = worker.Age,
                Salary = worker.Salary != null ? "Rs." + worker.Salary.ToString() : "Not Set",
                Bio = worker.Bio,
                AvailableStatus = worker.AvailableStatus ?? true,
                Radius = worker.Radius,
                Latitude = worker.Latitude,
                Longitude = worker.Longitude,
                Rating = reviews.Any() ? Math.Round(reviews.Average(r => (double)r.Rating), 1) : 0.0,
                ReviewCount = reviews.Count,
                Categories = catNames,
                Experiences = worker.Experiences.Select(e => new ExperienceVM { WorkAt = e.WorkAt, ExpDetail = e.ExpDetail, Duration = e.Duration }).ToList(),
                Slots = slots,
                Reviews = reviews,
                Habits = habits
            };
            return View(vm);
        }

        // ── INTERVIEW REQUESTS (pending) ──────────────────────────────────────────
        public async Task<IActionResult> Requests()
        {
            int workerId = WorkerId;
            var pending = await _context.Interviews
                .Include(i => i.Client)
                .Where(i => i.WorkerId == workerId && (i.WorkerDecision == null || i.WorkerDecision == "Pending"))
                .Select(i => new InterviewRequestVM
                {
                    InterviewId = i.InterviewId,
                    OtherPartyId = i.Client != null ? i.Client.ClientId : 0,
                    OtherPartyName = i.Client != null ? i.Client.Name : "Client",
                    OtherPartyPicture = i.Client != null ? i.Client.Picture : null,
                    Location = i.Address ?? "N/A",
                    Time = i.InterviewDate != null ? i.InterviewDate.Value.ToString("MMM dd, hh:mm tt") : "Not Set",
                    Status = i.Status ?? "",
                    JobType = i.JobType ?? "Full-Time",
                    IsResidenceProvided = i.IsResidenceProvided
                }).ToListAsync();
            return View(pending);
        }

        public async Task<IActionResult> AcceptedRequests()
        {
            int workerId = WorkerId;
            var accepted = await _context.Interviews
                .Include(i => i.Client)
                .Where(i => i.WorkerId == workerId && i.WorkerDecision == "Accepted")
                .Select(i => new InterviewRequestVM
                {
                    InterviewId = i.InterviewId,
                    OtherPartyId = i.Client != null ? i.Client.ClientId : 0,
                    OtherPartyName = i.Client != null ? i.Client.Name : "Client",
                    OtherPartyPicture = i.Client != null ? i.Client.Picture : null,
                    Location = i.Address ?? "N/A",
                    Time = i.InterviewDate != null ? i.InterviewDate.Value.ToString("MMM dd, hh:mm tt") : "Not Set",
                    Status = i.Status ?? "",
                    JobType = i.JobType ?? "Full-Time"
                }).ToListAsync();
            return View(accepted);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DecideInterview(int id, string decision)
        {
            int workerId = WorkerId;
            var interview = await _context.Interviews.FirstOrDefaultAsync(i => i.InterviewId == id && i.WorkerId == workerId);
            if (interview == null) { TempData["Error"] = "Request not found."; return RedirectToAction("Requests"); }

            interview.WorkerDecision = decision;
            if (decision == "Accepted") interview.Status = "Pending";
            else if (decision == "Rejected") interview.Status = "Rejected";
            await _context.SaveChangesAsync();

            TempData["Success"] = $"Interview {decision.ToLower()}.";
            return RedirectToAction("Requests");
        }

        // ── JOB CONFIRMATIONS (offers) ────────────────────────────────────────────
        public async Task<IActionResult> JobConfirmations()
        {
            int workerId = WorkerId;
            var jobs = await _context.Hiring
                .Include(h => h.Interview).ThenInclude(i => i!.Client)
                .Where(h => h.Interview!.WorkerId == workerId)
                .Select(h => new InterviewRequestVM
                {
                    InterviewId = h.InterviewId ?? 0,
                    HiringId = h.HiringId,
                    OtherPartyId = h.Interview!.Client != null ? h.Interview.Client.ClientId : 0,
                    OtherPartyName = h.Interview.Client != null ? h.Interview.Client.Name : "Client",
                    OtherPartyPicture = h.Interview.Client != null ? h.Interview.Client.Picture : null,
                    Location = h.Address ?? "N/A",
                    JobType = h.Interview.JobType ?? "Full-Time",
                    Status = h.Interview.Status ?? "",
                    HiringWorkerDecision = h.WorkerDecision ?? "Pending",
                    HiringDecision = h.HiringDecision ?? "Pending"
                }).ToListAsync();
            return View(jobs);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RespondJobOffer(int interviewId, string decision)
        {
            var hiring = await _context.Hiring.Include(h => h.Interview).FirstOrDefaultAsync(h => h.InterviewId == interviewId);
            if (hiring == null) { TempData["Error"] = "Job offer not found."; return RedirectToAction("JobConfirmations"); }

            if (decision == "Accepted")
            {
                hiring.WorkerDecision = "Accepted";
                TempData["Success"] = "Job offer accepted! Awaiting client confirmation.";
            }
            else
            {
                hiring.WorkerDecision = "Rejected";
                if (hiring.Interview != null) hiring.Interview.Status = "JobRejected";
                TempData["Success"] = "Job offer declined.";
            }
            await _context.SaveChangesAsync();
            return RedirectToAction("JobConfirmations");
        }

        // ── LEAVE JOB (resignation) ───────────────────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> LeaveJob()
        {
            int workerId = WorkerId;
            var active = await _context.Interviews
                .Include(i => i.Client)
                .Where(i => i.WorkerId == workerId &&
                            i.WorkerDecision != "Rejected" &&
                            i.Status != "Rejected" && i.Status != "JobRejected" &&
                            i.Status != "Completed" && i.Status != "Terminated" && i.Status != "Resigned")
                .GroupJoin(_context.Hiring.Where(h => h.HiringDecision == "Accepted"),
                    i => i.InterviewId, h => h.InterviewId,
                    (i, hg) => new { Interview = i, HasAccepted = hg.Any() })
                .Where(x => x.HasAccepted || x.Interview.Status == "Approved" ||
                            x.Interview.Status == "Finalized" || x.Interview.Status == "Hired" || x.Interview.Status == "Accepted")
                .OrderByDescending(x => x.Interview.InterviewId)
                .Select(x => new ActiveJobVM
                {
                    InterviewId = x.Interview.InterviewId,
                    ClientName = x.Interview.Client != null ? x.Interview.Client.Name : "Employer",
                    Address = x.Interview.Client != null ? (x.Interview.Client.Address ?? "N/A") : "N/A"
                })
                .FirstOrDefaultAsync();

            return View(active);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> LeaveJob(int interviewId, string resignationReason, DateTime lastWorkingDate,
            int? rating, string? comment)
        {
            var interview = await _context.Interviews.Include(i => i.Worker).FirstOrDefaultAsync(i => i.InterviewId == interviewId);
            if (interview == null) { TempData["Error"] = "Job record not found."; return RedirectToAction("Dashboard"); }

            if (await _context.Resignations.AnyAsync(r => r.InterviewId == interviewId))
            {
                TempData["Error"] = "You have already submitted a resignation for this job.";
                return RedirectToAction("Dashboard");
            }

            _context.Resignations.Add(new Resignation
            {
                InterviewId = interviewId,
                ResignationReason = resignationReason,
                LastWorkingDate = DateOnly.FromDateTime(lastWorkingDate),
                SubmittedDate = DateTime.Now
            });
            interview.Status = "ResignationPending";
            if (interview.Worker != null) interview.Worker.AvailableStatus = true;

            if (rating.HasValue)
            {
                _context.Reviews.Add(new Review
                {
                    InterviewId = interviewId,
                    Rating = rating,
                    Comment = comment,
                    ReviewerRole = "Worker",
                    ReviewDate = DateTime.Now
                });
            }
            await _context.SaveChangesAsync();

            TempData["Success"] = "Resignation submitted successfully.";
            return RedirectToAction("Dashboard");
        }

        // ── TERMINATION VIEW (client fired the worker) ────────────────────────────
        public async Task<IActionResult> Termination()
        {
            int workerId = WorkerId;
            var termination = await _context.Terminations
                .Include(t => t.Interview).ThenInclude(i => i!.Client)
                .Where(t => t.Interview!.WorkerId == workerId)
                .OrderByDescending(t => t.TerminatedDate)
                .FirstOrDefaultAsync();

            if (termination == null) return View((object?)null);

            ViewBag.AlreadyReviewed = await _context.Reviews.AnyAsync(r =>
                r.InterviewId == termination.InterviewId && r.ReviewerRole == "Worker");
            return View(termination);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ReviewClient(int interviewId, int rating, string? comment)
        {
            bool already = await _context.Reviews.AnyAsync(r => r.InterviewId == interviewId && r.ReviewerRole == "Worker");
            if (!already)
            {
                _context.Reviews.Add(new Review
                {
                    InterviewId = interviewId,
                    Rating = rating,
                    Comment = comment,
                    ReviewerRole = "Worker",
                    ReviewDate = DateTime.Now
                });
                await _context.SaveChangesAsync();
            }
            TempData["Success"] = "Review submitted.";
            return RedirectToAction("Termination");
        }

        // ── HABITS ────────────────────────────────────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> Habits()
        {
            int workerId = WorkerId;
            ViewBag.AllHabits = await _context.Habits.Where(h => h.IsActive)
                .OrderBy(h => h.SortOrder).ThenBy(h => h.Name)
                .Select(h => new { h.HabitId, h.Name }).ToListAsync();
            var selected = await _context.WorkerHabits.Where(wh => wh.WorkerId == workerId)
                .Select(wh => wh.HabitId).ToListAsync();
            return View(selected);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Habits(List<int> habitIds)
        {
            int workerId = WorkerId;
            using var transaction = await _context.Database.BeginTransactionAsync();

            var wanted = (habitIds ?? new List<int>()).Where(id => id > 0).Distinct().ToList();
            var validIds = await _context.Habits.Where(h => wanted.Contains(h.HabitId)).Select(h => h.HabitId).ToListAsync();

            var existing = await _context.WorkerHabits.Where(wh => wh.WorkerId == workerId).ToListAsync();
            var toRemove = existing.Where(wh => !validIds.Contains(wh.HabitId)).ToList();
            if (toRemove.Any()) _context.WorkerHabits.RemoveRange(toRemove);

            var alreadyThere = existing.Select(wh => wh.HabitId).ToHashSet();
            foreach (var id in validIds.Where(id => !alreadyThere.Contains(id)))
                _context.WorkerHabits.Add(new WorkerHabits { WorkerId = workerId, HabitId = id, CreatedDate = DateTime.Now });

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            TempData["Success"] = "Habits updated.";
            return RedirectToAction("Habits");
        }

        // ── LOCATION ──────────────────────────────────────────────────────────────
        public async Task<IActionResult> Location()
        {
            var worker = await _context.Workers.FindAsync(WorkerId);
            if (worker == null) return NotFound();
            return View(worker);
        }

        // ── AJAX actions ──────────────────────────────────────────────────────────
        [HttpPost]
        public async Task<IActionResult> UpdateDutyStatus([FromBody] BoolDto dto)
        {
            var worker = await _context.Workers.FindAsync(WorkerId);
            if (worker == null) return Json(new { ok = false });
            worker.AvailableStatus = dto.Value;
            await _context.SaveChangesAsync();
            return Json(new { ok = true, isAvailable = worker.AvailableStatus });
        }

        [HttpPost]
        public async Task<IActionResult> UpdateRadius([FromBody] IntDto dto)
        {
            if (dto.Value < 1 || dto.Value > 70) return Json(new { ok = false, message = "Invalid radius." });
            var worker = await _context.Workers.FindAsync(WorkerId);
            if (worker == null) return Json(new { ok = false });
            worker.Radius = dto.Value;
            await _context.SaveChangesAsync();
            return Json(new { ok = true, radius = worker.Radius });
        }

        [HttpPost]
        public async Task<IActionResult> AddTimeSlot([FromBody] SlotDto dto)
        {
            if (!TimeSpan.TryParse(dto.StartTime, out var start) || !TimeSpan.TryParse(dto.EndTime, out var end))
                return Json(new { ok = false, message = "Invalid time format." });
            if (start >= end) return Json(new { ok = false, message = "Start time must be before end time." });

            int workerId = WorkerId;
            bool overlapping = await _context.WorkerTimeSlots.AnyAsync(s => s.WorkerId == workerId &&
                ((start >= s.StartTime && start < s.EndTime) ||
                 (end > s.StartTime && end <= s.EndTime) ||
                 (start <= s.StartTime && end >= s.EndTime)));
            if (overlapping) return Json(new { ok = false, message = "The selected time slot overlaps an existing schedule." });

            var slot = new WorkerTimeSlots { WorkerId = workerId, StartTime = start, EndTime = end };
            _context.WorkerTimeSlots.Add(slot);
            await _context.SaveChangesAsync();
            return Json(new { ok = true, id = slot.Id, startTime = start.ToString(@"hh\:mm"), endTime = end.ToString(@"hh\:mm") });
        }

        [HttpPost]
        public async Task<IActionResult> DeleteTimeSlot([FromBody] IntDto dto)
        {
            var slot = await _context.WorkerTimeSlots.FindAsync(dto.Value);
            if (slot == null || slot.WorkerId != WorkerId) return Json(new { ok = false });
            _context.WorkerTimeSlots.Remove(slot);
            await _context.SaveChangesAsync();
            return Json(new { ok = true });
        }

        [HttpPost]
        public async Task<IActionResult> UpdateLocation([FromBody] LatLng dto)
        {
            var worker = await _context.Workers.FindAsync(WorkerId);
            if (worker == null) return Json(new { ok = false });
            worker.Latitude = (decimal)dto.Latitude;
            worker.Longitude = (decimal)dto.Longitude;
            await _context.SaveChangesAsync();
            return Json(new { ok = true });
        }

        public class BoolDto { public bool Value { get; set; } }
        public class IntDto { public int Value { get; set; } }
        public class SlotDto { public string StartTime { get; set; } = ""; public string EndTime { get; set; } = ""; }
        public class LatLng { public double Latitude { get; set; } public double Longitude { get; set; } }
    }
}
