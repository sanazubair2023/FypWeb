using System.Security.Claims;
using Fyp_Backend.Models;
using FypWeb.Helpers;
using FypWeb.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;

namespace FypWeb.Controllers
{
    [Authorize(Roles = "Worker")]
    public class WorkerController : Controller
    {
        private readonly Fyp1Context _context;
        private readonly ImageService _images;
        public WorkerController(Fyp1Context context, ImageService images)
        {
            _context = context;
            _images = images;
        }

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

            // Skills tab data (RN WorkerDashboardScreen): primary category = the
            // first junction row's category; its skills are "primary skills",
            // every other category groups as secondary/part-time services.
            var junctionRows = await (
                from wc in _context.WorkerCategories.Where(wc => wc.WorkerId == workerId)
                join c in _context.Categories on wc.CategoryId equals c.CategoryId into cg
                from c in cg.DefaultIfEmpty()
                join s in _context.Skills on wc.SkillsId equals s.SkillsId into sg
                from s in sg.DefaultIfEmpty()
                select new { CategoryId = wc.CategoryId, CategoryName = c != null ? c.CategoryName : "", SkillName = s != null ? s.SkillName : "" }
                ).ToListAsync();

            string primaryRole = "General Worker";
            var primarySkills = new List<string>();
            var partTimeSkills = new List<PartTimeSkillGroupVM>();
            int? primaryCatId = junctionRows.Select(r => (int?)r.CategoryId).FirstOrDefault();
            if (primaryCatId.HasValue)
            {
                var primaryName = junctionRows.Where(r => r.CategoryId == primaryCatId.Value)
                    .Select(r => r.CategoryName).FirstOrDefault();
                if (!string.IsNullOrEmpty(primaryName)) primaryRole = primaryName;
                primarySkills = junctionRows.Where(r => r.CategoryId == primaryCatId.Value && !string.IsNullOrEmpty(r.SkillName))
                    .Select(r => r.SkillName!).Distinct().ToList();
                partTimeSkills = junctionRows.Where(r => r.CategoryId != primaryCatId.Value)
                    .GroupBy(r => r.CategoryName)
                    .Select(g => new PartTimeSkillGroupVM
                    {
                        CategoryName = string.IsNullOrEmpty(g.Key) ? "Additional Service" : g.Key,
                        Skills = g.Where(x => !string.IsNullOrEmpty(x.SkillName)).Select(x => x.SkillName!).Distinct().ToList()
                    }).ToList();
            }

            var slots = await _context.WorkerTimeSlots.Where(s => s.WorkerId == workerId)
                .OrderBy(s => s.StartTime)
                .Select(s => new SlotVM { Id = s.Id, StartTime = s.StartTime.ToString(@"hh\:mm"), EndTime = s.EndTime.ToString(@"hh\:mm") })
                .ToListAsync();

            // Reviews OF this worker, written BY clients — the card shows the
            // client's name next to each review (RN ReviewCard reviewerName).
            // Detail reviews: per-criterion breakdown + how the contract ended.
            var reviewRows = await _context.Reviews
                .Where(r => r.Interview != null && r.Interview.WorkerId == workerId && r.ReviewerRole == "Client")
                .OrderByDescending(r => r.ReviewDate)
                .Select(r => new
                {
                    r.InterviewId,
                    Rating = r.Rating ?? 0m,
                    r.Comment,
                    ReviewerName = r.Interview!.Client != null ? r.Interview.Client.Name : "Anonymous",
                    r.ReviewDate,
                    EndType = r.Interview.Status,
                    Criteria = r.ReviewCriteriaRatings
                        .OrderBy(c => c.Criteria.SortOrder).ThenBy(c => c.CriteriaId)
                        .Select(c => new CriteriaScoreVM { Name = c.Criteria.CriteriaName, Score = c.Score })
                        .ToList()
                }).ToListAsync();

            // "Worked: …" line on each review card — the contract window behind
            // the interview (hire date → termination/resignation date).
            var reviewWindows = await LoadContractWindowsAsync(
                reviewRows.Where(r => r.InterviewId.HasValue).Select(r => r.InterviewId!.Value).Distinct().ToList(),
                reviewRows.Where(r => r.InterviewId.HasValue)
                    .GroupBy(r => r.InterviewId!.Value)
                    .ToDictionary(g => g.Key, g => g.Max(r => r.ReviewDate)));

            var reviews = reviewRows.Select(r => new ReviewVM
            {
                Rating = r.Rating,
                Comment = r.Comment,
                ReviewerRole = "Client",
                ReviewerName = r.ReviewerName ?? "Anonymous",
                ReviewDate = r.ReviewDate != null ? r.ReviewDate.Value.ToString("dd MMM yyyy") : "",
                WorkedPeriod = r.InterviewId.HasValue && reviewWindows.TryGetValue(r.InterviewId.Value, out var w)
                    ? FormatWorkedPeriod(w.From, w.To) : null,
                EndType = r.EndType,
                Criteria = r.Criteria
            }).ToList();

            // Overview counters (RN WorkerDashboardScreen notification cards).
            int pendingRequestCount = await _context.Interviews
                .CountAsync(i => i.WorkerId == workerId && (i.WorkerDecision == null || i.WorkerDecision == "Pending"));
            int jobNotificationCount = await _context.Hiring
                .CountAsync(h => h.InterviewId != null && h.Interview.WorkerId == workerId && h.WorkerDecision == "Pending");
            int terminationCount = await _context.Interviews
                .CountAsync(i => i.WorkerId == workerId && i.Status == "Terminated");

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
                Habits = habits,
                PrimaryRole = primaryRole,
                PrimarySkills = primarySkills,
                PartTimeSkills = partTimeSkills,
                PendingRequestCount = pendingRequestCount,
                JobNotificationCount = jobNotificationCount,
                TerminationCount = terminationCount
            };
            return View(vm);
        }

        // ── CONTRACT WINDOW (port of api Dashboard LoadContractWindowsAsync) ────────
        // start = Hiring.HiringDate (falls back to Interview.InterviewDate)
        // end   = Termination.TerminatedDate, else Resignation.LastWorkingDate
        //        (falls back to Resignation.SubmittedDate, then the review date)
        private async Task<Dictionary<int, (DateTime? From, DateTime? To)>> LoadContractWindowsAsync(
            List<int> interviewIds, Dictionary<int, DateTime?> fallbackEndDates)
        {
            var windows = new Dictionary<int, (DateTime? From, DateTime? To)>();
            if (interviewIds == null || interviewIds.Count == 0) return windows;

            var hireDates = (await _context.Hiring
                .Where(h => h.InterviewId != null && interviewIds.Contains(h.InterviewId.Value))
                .Select(h => new { InterviewId = h.InterviewId!.Value, h.HiringDate })
                .ToListAsync())
                .GroupBy(x => x.InterviewId)
                .ToDictionary(g => g.Key, g => g.Min(x => x.HiringDate));

            var interviewDates = (await _context.Interviews
                .Where(i => interviewIds.Contains(i.InterviewId))
                .Select(i => new { i.InterviewId, i.InterviewDate })
                .ToListAsync())
                .ToDictionary(x => x.InterviewId, x => x.InterviewDate);

            var resignations = await _context.Resignations
                .Where(r => r.InterviewId != null && interviewIds.Contains(r.InterviewId.Value))
                .Select(r => new { InterviewId = r.InterviewId!.Value, r.SubmittedDate, r.LastWorkingDate })
                .ToListAsync();

            var terminations = await _context.Terminations
                .Where(t => t.InterviewId != null && interviewIds.Contains(t.InterviewId.Value))
                .Select(t => new { InterviewId = t.InterviewId!.Value, t.TerminatedDate })
                .ToListAsync();

            foreach (var id in interviewIds.Distinct())
            {
                DateTime? from = hireDates.TryGetValue(id, out var hireDate) ? hireDate : null;
                if (from == null && interviewDates.TryGetValue(id, out var interviewDate)) from = interviewDate;

                DateTime? to = null;

                var termination = terminations
                    .Where(t => t.InterviewId == id && t.TerminatedDate != null)
                    .OrderByDescending(t => t.TerminatedDate)
                    .FirstOrDefault();
                if (termination != null)
                {
                    to = termination.TerminatedDate!.Value.ToDateTime(TimeOnly.MinValue);
                }
                else
                {
                    var resignation = resignations
                        .Where(r => r.InterviewId == id)
                        .OrderByDescending(r => r.SubmittedDate)
                        .FirstOrDefault();
                    if (resignation != null)
                    {
                        to = resignation.LastWorkingDate != default
                            ? resignation.LastWorkingDate.ToDateTime(TimeOnly.MinValue)
                            : resignation.SubmittedDate;
                    }
                }

                if (to == null && fallbackEndDates != null && fallbackEndDates.TryGetValue(id, out var reviewDate))
                    to = reviewDate;

                if (from != null || to != null) windows[id] = (from, to);
            }

            return windows;
        }

        private static string? FormatWorkedSpan(DateTime? from, DateTime? to)
        {
            if (from == null || to == null) return null;
            var f = from.Value.Date;
            var t = to.Value.Date;
            if (t < f) t = f;
            return f.Year == t.Year
                ? $"{f:dd MMM} – {t:dd MMM yyyy}"
                : $"{f:dd MMM yyyy} – {t:dd MMM yyyy}";
        }

        private static string? FormatWorkedPeriod(DateTime? from, DateTime? to)
        {
            var span = FormatWorkedSpan(from, to);
            if (span == null || from == null || to == null) return null;
            var days = (to.Value.Date - from.Value.Date).Days;
            if (days < 0) days = 0;
            return span + (days <= 1 ? " (1 day)" : $" ({days} days)");
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

            // The client's criteria-scored closing review of this worker
            // (RN WorkerTerminationScreen review card).
            var reviewRow = await _context.Reviews
                .Where(r => r.InterviewId == termination.InterviewId && r.ReviewerRole == "Client")
                .OrderByDescending(r => r.ReviewDate)
                .Select(r => new
                {
                    Rating = r.Rating ?? 0m,
                    r.Comment,
                    r.ReviewDate,
                    ReviewerName = r.Interview!.Client != null ? r.Interview.Client.Name : "Client",
                    Criteria = r.ReviewCriteriaRatings
                        .OrderBy(c => c.Criteria.SortOrder).ThenBy(c => c.CriteriaId)
                        .Select(c => new CriteriaScoreVM { Name = c.Criteria.CriteriaName, Score = c.Score })
                        .ToList()
                }).FirstOrDefaultAsync();

            if (reviewRow != null)
            {
                var windows = await LoadContractWindowsAsync(
                    new List<int> { termination.InterviewId ?? 0 },
                    new Dictionary<int, DateTime?> { { termination.InterviewId ?? 0, reviewRow.ReviewDate } });
                windows.TryGetValue(termination.InterviewId ?? 0, out var w);

                ViewBag.ClientReview = new ReviewVM
                {
                    Rating = reviewRow.Rating,
                    Comment = reviewRow.Comment,
                    ReviewerRole = "Client",
                    ReviewerName = reviewRow.ReviewerName ?? "Client",
                    ReviewDate = reviewRow.ReviewDate != null ? reviewRow.ReviewDate.Value.ToString("dd MMM yyyy") : "",
                    WorkedPeriod = FormatWorkedPeriod(w.From, w.To),
                    EndType = "Terminated",
                    Criteria = reviewRow.Criteria
                };
            }
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

        // ── EDIT PROFILE ───────────────────────────────────────────────────────────
        // Ported from the RN flow WorkerDashboardScreen → Signup (isEdit: true) →
        // POST api/AccountCreation/UpdateWorker (NEW split contract):
        //   • skillsJson      = [{ CategoryId, SkillsId }] → Worker_Category (full replace)
        //   • experiencesJson = [{ WorkAt, ExpDetail, Duration }] → Experience (full replace)
        //   • habitsJson semantics unchanged (empty = clear)
        // Cnic is immutable (login key — the API never updates the column),
        // photo/password optional in edit mode, and the RN edit form requires
        // at least one sub-skill before saving.
        [HttpGet]
        public async Task<IActionResult> EditProfile()
        {
            var worker = await _context.Workers.FindAsync(WorkerId);
            if (worker == null) return NotFound();

            await PopulateEditLookupsAsync();
            return View(await BuildEditModelAsync(worker));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditProfile(EditWorkerProfileVM model)
        {
            if (string.IsNullOrWhiteSpace(model.Name) || string.IsNullOrWhiteSpace(model.Phone)
                || string.IsNullOrWhiteSpace(model.Address))
            {
                ModelState.AddModelError(string.Empty, "Please fill out all fundamental profile details.");
            }

            if (!string.IsNullOrEmpty(model.Password) && model.Password != model.ConfirmPassword)
            {
                ModelState.AddModelError(string.Empty, "Passwords do not match.");
            }

            // Skills (junction rows) — the RN edit form blocks an empty list.
            List<SkillInput> skills = new();
            if (!string.IsNullOrWhiteSpace(model.SkillsJson))
            {
                try { skills = JsonConvert.DeserializeObject<List<SkillInput>>(model.SkillsJson) ?? new(); }
                catch { ModelState.AddModelError(string.Empty, "Skills payload was not in the expected format."); }
            }
            skills = skills.Where(s => s.CategoryId > 0 && s.SkillsId > 0)
                           .GroupBy(s => (s.CategoryId, s.SkillsId)).Select(g => g.First()).ToList();
            if (skills.Count == 0)
            {
                ModelState.AddModelError(string.Empty, "Please add at least one sub-skill to proceed.");
            }

            // Experience — plain work history now (no category/skill fields).
            List<ExperienceInput> experiences = new();
            if (!string.IsNullOrWhiteSpace(model.ExperiencesJson))
            {
                try { experiences = JsonConvert.DeserializeObject<List<ExperienceInput>>(model.ExperiencesJson) ?? new(); }
                catch { ModelState.AddModelError(string.Empty, "Experience payload was not in the expected format."); }
            }

            var existing = await _context.Workers.FindAsync(WorkerId);
            if (existing == null) return NotFound();

            if (!ModelState.IsValid)
            {
                await PopulateEditLookupsAsync();
                model.Cnic = existing.Cnic;
                model.CurrentPicture = existing.Picture;
                return View(model);
            }

            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                // Optional new photo — identifier mirrors UpdateWorker (Cnic).
                if (model.PictureFile != null)
                {
                    string identifier = model.Cnic ?? existing.Cnic;
                    string? imagePath = await _images.SaveImageAsync(model.PictureFile, identifier);
                    if (imagePath == "Invalid")
                    {
                        ModelState.AddModelError(string.Empty, "Only .jpg, .jpeg, and .png files are allowed.");
                        await PopulateEditLookupsAsync();
                        model.Cnic = existing.Cnic;
                        model.CurrentPicture = existing.Picture;
                        return View(model);
                    }
                    if (imagePath != null) existing.Picture = imagePath;
                }

                existing.Name = model.Name;
                existing.Phone = model.Phone;
                existing.Address = model.Address;
                existing.Age = model.Age;
                existing.Salary = model.Salary;
                existing.Gender = model.Gender;
                existing.Bio = model.Bio;

                // Blank password keeps the current one; "********" is the
                // masked sentinel the API ignores (legacy RN prefill).
                if (!string.IsNullOrEmpty(model.Password) && model.Password != "********")
                {
                    existing.Password = model.Password;
                }

                // Skills: full replace of the Worker_Category junctions.
                var oldCats = await _context.WorkerCategories.Where(wc => wc.WorkerId == WorkerId).ToListAsync();
                _context.WorkerCategories.RemoveRange(oldCats);
                await _context.SaveChangesAsync();

                foreach (var s in skills)
                {
                    _context.WorkerCategories.Add(new WorkerCategory
                    {
                        WorkerId = WorkerId,
                        CategoryId = s.CategoryId,
                        SkillsId = s.SkillsId
                    });
                }

                // Experience: full replace — history rows only.
                var oldExps = await _context.Experiences.Where(e => e.WorkerId == WorkerId).ToListAsync();
                _context.Experiences.RemoveRange(oldExps);
                await _context.SaveChangesAsync();

                foreach (var exp in experiences)
                {
                    _context.Experiences.Add(new Experience
                    {
                        WorkerId = WorkerId,
                        WorkAt = exp.WorkAt,
                        ExpDetail = exp.ExpDetail,
                        Duration = exp.Duration
                    });
                }

                // Habits sync — identical to UpdateWorker's habitsJson handling:
                // unknown ids ignored, unselected removed, newly selected added.
                var wanted = (model.HabitIds ?? new List<int>()).Where(id => id > 0).Distinct().ToList();
                var validIds = await _context.Habits
                    .Where(h => wanted.Contains(h.HabitId))
                    .Select(h => h.HabitId)
                    .ToListAsync();

                var oldHabits = await _context.WorkerHabits.Where(wh => wh.WorkerId == WorkerId).ToListAsync();
                var removeHabits = oldHabits.Where(wh => !validIds.Contains(wh.HabitId)).ToList();
                if (removeHabits.Any()) _context.WorkerHabits.RemoveRange(removeHabits);

                var haveIds = oldHabits.Select(wh => wh.HabitId).ToHashSet();
                foreach (var id in validIds.Where(id => !haveIds.Contains(id)))
                {
                    _context.WorkerHabits.Add(new WorkerHabits
                    {
                        WorkerId = WorkerId,
                        HabitId = id,
                        CreatedDate = DateTime.Now
                    });
                }

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                ModelState.AddModelError(string.Empty, "Update failed: " + (ex.InnerException?.Message ?? ex.Message));
                await PopulateEditLookupsAsync();
                model.Cnic = existing.Cnic;
                model.CurrentPicture = existing.Picture;
                return View(model);
            }

            // Keep the cookie (topbar name/photo claims) in sync.
            await RefreshSignInAsync(existing.WorkerId, existing.Name, existing.Picture);

            TempData["Success"] = "Profile updated successfully!";
            return RedirectToAction("Dashboard");
        }

        private async Task PopulateEditLookupsAsync()
        {
            ViewBag.AllHabits = await _context.Habits.Where(h => h.IsActive)
                .OrderBy(h => h.SortOrder).ThenBy(h => h.Name)
                .Select(h => new { h.HabitId, h.Name }).ToListAsync();
        }

        private async Task<EditWorkerProfileVM> BuildEditModelAsync(Worker worker)
        {
            var model = new EditWorkerProfileVM
            {
                Name = worker.Name,
                Cnic = worker.Cnic,
                Phone = worker.Phone,
                Address = worker.Address,
                Age = worker.Age,
                Salary = worker.Salary,
                Gender = string.Equals(worker.Gender, "Female", StringComparison.OrdinalIgnoreCase) ? "Female" : "Male",
                Bio = worker.Bio,
                CurrentPicture = worker.Picture,
                HabitIds = await _context.WorkerHabits
                    .Where(wh => wh.WorkerId == worker.WorkerId)
                    .Select(wh => wh.HabitId).ToListAsync(),
                SkillsJson = "[]",
                ExperiencesJson = "[]"
            };

            // Existing skills = the Worker_Category junction rows (with names so
            // the Add Skills screen can pre-tick chips and the form can show a
            // summary) — mirrors the API's rawWorkerCategories.
            var skills = await (
                from wc in _context.WorkerCategories.Where(wc => wc.WorkerId == worker.WorkerId)
                join c in _context.Categories on wc.CategoryId equals c.CategoryId into cg
                from c in cg.DefaultIfEmpty()
                join s in _context.Skills on wc.SkillsId equals s.SkillsId into sg
                from s in sg.DefaultIfEmpty()
                orderby wc.CategoryId, wc.SkillsId
                select new SkillInput
                {
                    CategoryId = wc.CategoryId,
                    SkillsId = wc.SkillsId,
                    CategoryName = c != null ? c.CategoryName : "",
                    SkillName = s != null ? s.SkillName : ""
                }).ToListAsync();

            model.SkillsJson = JsonConvert.SerializeObject(skills);

            // Existing experiences — work history only.
            var experiences = await _context.Experiences
                .Where(e => e.WorkerId == worker.WorkerId)
                .OrderBy(e => e.ExperienceId)
                .Select(e => new ExperienceInput
                {
                    WorkAt = e.WorkAt,
                    ExpDetail = e.ExpDetail,
                    Duration = e.Duration
                }).ToListAsync();

            model.ExperiencesJson = JsonConvert.SerializeObject(experiences);
            return model;
        }

        // Re-issues the auth cookie after a profile edit so the sidebar shows
        // the new name/photo without a re-login (same claims as Account/Login).
        private async Task RefreshSignInAsync(int workerId, string? name, string? picture)
        {
            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, workerId.ToString()),
                new(ClaimTypes.Name, string.IsNullOrWhiteSpace(name) ? "Worker" : name),
                new(ClaimTypes.Role, "Worker"),
                new("Picture", picture ?? ""),
                new("Email", "")
            };
            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            await HttpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                new ClaimsPrincipal(identity),
                new AuthenticationProperties { IsPersistent = true });
        }

        // ── COURSES: CATALOGUE (RN WorkerCourseCatalogueScreen) ─────────────────────
        // GET api/worker/courses — published, non-deleted courses that have not
        // ended, with this worker's enrollment status for the button state.
        public async Task<IActionResult> Courses(string? search)
        {
            int workerId = WorkerId;
            var today = DateOnly.FromDateTime(DateTime.UtcNow);

            var q = _context.Courses.AsNoTracking()
                .Where(x => !x.IsDeleted && x.IsPublished && x.EndDate >= today);

            if (!string.IsNullOrWhiteSpace(search))
                q = q.Where(x => x.CourseName.Contains(search) || (x.Description != null && x.Description.Contains(search)));

            var rows = await q.OrderBy(x => x.StartDate)
                .Select(x => new WorkerCourseCardVM
                {
                    CourseId = x.CourseID,
                    CourseName = x.CourseName,
                    CompanyName = x.Company.CompanyName,
                    StartDate = x.StartDate.ToString("dd MMM yyyy"),
                    EndDate = x.EndDate.ToString("dd MMM yyyy"),
                    ClassTimings = x.ClassTimings,
                    Description = x.Description,
                    EnrollmentStatus = x.Enrollments.Where(e => e.WorkerID == workerId)
                        .Select(e => e.Status).FirstOrDefault()
                }).ToListAsync();

            ViewBag.Search = search;
            return View(rows);
        }

        // POST api/worker/courses/{id}/enroll
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Enroll(int courseId)
        {
            int workerId = WorkerId;
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var course = await _context.Courses.FirstOrDefaultAsync(
                x => x.CourseID == courseId && !x.IsDeleted && x.IsPublished);
            if (course == null) { TempData["Error"] = "Course is not available."; return RedirectToAction("Courses"); }
            if (course.EndDate < today) { TempData["Error"] = "This course has ended."; return RedirectToAction("Courses"); }

            var existing = await _context.CourseEnrollments
                .FirstOrDefaultAsync(x => x.CourseID == courseId && x.WorkerID == workerId);
            if (existing != null)
            {
                TempData["Error"] = "You are already enrolled in this course (" + existing.Status + ").";
                return RedirectToAction("Courses");
            }

            _context.CourseEnrollments.Add(new CourseEnrollment
            {
                CourseID = courseId,
                WorkerID = workerId,
                Status = "Enrolled",
                EnrolledAtUtc = DateTime.UtcNow
            });
            await _context.SaveChangesAsync();
            TempData["Success"] = "Enrollment successful.";
            return RedirectToAction("Courses");
        }

        // ── COURSES: MY COURSES (RN WorkerMyCoursesScreen) ──────────────────────────
        // GET api/worker/enrollments?status=
        public async Task<IActionResult> MyCourses(string? status)
        {
            int workerId = WorkerId;
            var q = _context.CourseEnrollments.AsNoTracking().Where(x => x.WorkerID == workerId);
            if (!string.IsNullOrWhiteSpace(status)) q = q.Where(x => x.Status == status);

            var rows = await q.OrderByDescending(x => x.EnrolledAtUtc)
                .Select(x => new MyCourseVM
                {
                    EnrollmentId = x.EnrollmentID,
                    Status = x.Status,
                    CourseName = x.Course.CourseName,
                    CompanyName = x.Course.Company.CompanyName,
                    StartDate = x.Course.StartDate.ToString("dd MMM yyyy"),
                    EndDate = x.Course.EndDate.ToString("dd MMM yyyy"),
                    ClassTimings = x.Course.ClassTimings,
                    CertificateId = x.Certificate == null ? (int?)null : x.Certificate.CertificateID,
                    CertificateCode = x.Certificate == null ? null : x.Certificate.CertificateCode
                }).ToListAsync();

            ViewBag.Status = status ?? "";
            return View(rows);
        }

        // POST api/worker/enrollments/{id}/withdraw
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Withdraw(int enrollmentId)
        {
            int workerId = WorkerId;
            var item = await _context.CourseEnrollments
                .FirstOrDefaultAsync(x => x.EnrollmentID == enrollmentId && x.WorkerID == workerId);
            if (item == null) { TempData["Error"] = "Enrollment not found."; return RedirectToAction("MyCourses"); }
            if (item.Status == "Completed")
            {
                TempData["Error"] = "A completed course cannot be withdrawn.";
                return RedirectToAction("MyCourses");
            }

            item.Status = "Withdrawn";
            item.WithdrawnAtUtc = DateTime.UtcNow;
            item.CompletionDateUtc = null;
            item.UpdatedAtUtc = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            TempData["Success"] = "Enrollment withdrawn.";
            return RedirectToAction("MyCourses");
        }

        // ── COURSES: DIGITAL CERTIFICATE (RN CourseCertificateScreen) ───────────────
        // GET api/course-certificates/{id} — role-scoped: a worker sees only own.
        public async Task<IActionResult> Certificate(int id)
        {
            int workerId = WorkerId;
            var cert = await _context.CourseCertificates.AsNoTracking()
                .Where(c => c.CertificateID == id && c.WorkerID == workerId)
                .Select(c => new CertificateVM
                {
                    CertificateId = c.CertificateID,
                    CertificateCode = c.CertificateCode,
                    WorkerName = c.Worker.Name ?? "Worker",
                    CompanyName = c.Company.CompanyName,
                    CourseName = c.Course.CourseName,
                    IssuedDate = c.IssuedDateUtc.ToString("dd MMM yyyy"),
                    IsValid = !c.IsRevoked
                }).FirstOrDefaultAsync();

            if (cert == null) return NotFound();
            return View(cert);
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
