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
    [Authorize(Roles = "Client")]
    public class ClientController : Controller
    {
        private readonly Fyp1Context _context;
        private readonly ImageService _images;
        private readonly PasswordService _passwords;
        private readonly ContractRules _rules;
        private readonly ResignationWorkflowService _workflow;
        private readonly ILogger<ClientController> _logger;

        public ClientController(Fyp1Context context, ImageService images, PasswordService passwords,
            ContractRules rules, ResignationWorkflowService workflow, ILogger<ClientController> logger)
        {
            _context = context;
            _images = images;
            _passwords = passwords;
            _rules = rules;
            _workflow = workflow;
            _logger = logger;
        }

        private int ClientId => User.GetId();

        // ── DASHBOARD ─────────────────────────────────────────────────────────────
        public async Task<IActionResult> Dashboard()
        {
            int clientId = ClientId;
            await _workflow.ProcessDueAsync(clientId: clientId);

            ViewBag.PendingCount = await _context.Interviews.CountAsync(i => i.ClientId == clientId && i.Status == "Pending");
            ViewBag.PendingJobRequestsCount = await _context.Hiring.CountAsync(h => h.Interview != null &&
                h.Interview.ClientId == clientId && h.HiringDecision == "Pending" &&
                (h.WorkerDecision == "Accepted" || h.WorkerDecision == "Rejected") && h.Interview.Status != "Terminated" &&
                h.Interview.Status != "Resigned" && h.Interview.Status != "Completed");
            ViewBag.PendingResignationsCount = await _context.Resignations.CountAsync(r => r.Interview != null &&
                r.Interview.ClientId == clientId && r.Interview.Status != "Terminated" &&
                (r.Interview.Status != "Resigned" || r.AcceptanceMode == "Automatic") &&
                !_context.Reviews.Any(review => review.InterviewId == r.InterviewId && review.ReviewerRole == "Client"));

            var hired = await _context.Hiring
                .Include(h => h.Interview).ThenInclude(i => i!.Worker)
                .Where(h => h.Interview!.ClientId == clientId &&
                            h.WorkerDecision == "Accepted" &&
                            h.HiringDecision == "Accepted")
                .Select(h => new
                {
                    workerId = h.Interview!.WorkerId,
                    interviewId = h.InterviewId,
                    name = h.Interview.Worker!.Name,
                    picture = h.Interview.Worker.Picture,
                    location = h.Address ?? h.Interview.Address,
                    jobType = h.Interview.JobType ?? "Full-Time",
                    status = h.Interview.Status,
                    slotStart = h.Interview.Slot != null ? (TimeSpan?)h.Interview.Slot.StartTime : null,
                    slotEnd = h.Interview.Slot != null ? (TimeSpan?)h.Interview.Slot.EndTime : null,
                    isExclusive = h.Interview.IsExclusive && h.Interview.JobType == "Full-Time",
                    resignationId = _context.Resignations.Where(r => r.InterviewId == h.InterviewId)
                        .OrderByDescending(r => r.SubmittedDate).ThenByDescending(r => r.ResignationId)
                        .Select(r => (int?)r.ResignationId).FirstOrDefault(),
                    hiringDate = h.HiringDate
                })
                .ToListAsync();

            var cards = hired.Select(h => new InterviewRequestVM
            {
                InterviewId = h.interviewId ?? 0,
                OtherPartyId = h.workerId ?? 0,
                OtherPartyName = h.name ?? "Worker",
                OtherPartyPicture = h.picture,
                Location = h.location ?? "N/A",
                JobType = h.jobType,
                SlotStart = h.slotStart.HasValue ? DateTime.Today.Add(h.slotStart.Value).ToString("hh\\:mm tt") : null,
                SlotEnd = h.slotEnd.HasValue ? DateTime.Today.Add(h.slotEnd.Value).ToString("hh\\:mm tt") : null,
                Status = h.status switch
                {
                    "ResignationPending" => "Pending Resignation",
                    "Resigned" => "Resigned",
                    "Terminated" => "Terminated",
                    "Completed" => "Completed",
                    _ => "On Work"
                },
                IsExclusive = h.isExclusive,
                ResignationId = h.resignationId,
                HiringDate = h.hiringDate?.ToString("dd MMM yyyy")
            }).ToList();

            ViewBag.HiredCount = cards.Count(c => c.Status is "On Work" or "Pending Resignation");
            return View(cards);
        }

        // ── FIND SERVICE (search + filters) ───────────────────────────────────────
        public async Task<IActionResult> FindService(
            string? search, List<string>? categories, string? gender, string? city,
            List<string>? subSkills, List<int>? habitIds)
        {
            var currentCity = await GetCurrentCityAsync();
            var selectedCity = !string.IsNullOrWhiteSpace(city) ? city.Trim() : currentCity;
            var cityFilter = !string.IsNullOrWhiteSpace(selectedCity) &&
                !selectedCity.Equals("All", StringComparison.OrdinalIgnoreCase) ? selectedCity : null;

            IQueryable<Worker> query = _rules.DiscoverableByClient(_context.Workers.AsNoTracking(), ClientId);
            if (categories != null && categories.Any() && !categories.Contains("All"))
            {
                var normalized = categories.Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c.Trim()).ToList();
                var categoryIds = normalized.Where(c => int.TryParse(c, out _)).Select(int.Parse).ToList();
                var categoryNames = normalized.Where(c => !int.TryParse(c, out _)).ToList();
                query = query.Where(w => _context.WorkerCategories.Any(wc => wc.WorkerId == w.WorkerId &&
                    _context.Categories.Any(c => c.CategoryId == wc.CategoryId &&
                        (categoryIds.Contains(c.CategoryId) || categoryNames.Contains(c.CategoryName)))));
            }
            if (!string.IsNullOrEmpty(gender) && gender != "Both") query = query.Where(w => w.Gender == gender);
            if (cityFilter != null) query = query.Where(w => w.Address != null && w.Address.Contains(cityFilter));
            if (!string.IsNullOrWhiteSpace(search)) query = query.Where(w => w.Name != null && w.Name.Contains(search));
            if (subSkills != null) foreach (var skillName in subSkills.Where(x => !string.IsNullOrWhiteSpace(x)))
                query = query.Where(w => _context.WorkerCategories.Any(wc => wc.WorkerId == w.WorkerId &&
                    _context.Skills.Any(s => s.SkillsId == wc.SkillsId && s.SkillName == skillName)));
            if (habitIds != null) foreach (var wanted in habitIds.Where(id => id > 0).Distinct())
                query = query.Where(w => _context.WorkerHabits.Any(wh => wh.WorkerId == w.WorkerId && wh.HabitId == wanted));

            var workers = await query.OrderBy(w => w.Name).ToListAsync();
            var ids = workers.Select(w => w.WorkerId).ToList();
            var categoryRows = await _context.WorkerCategories.Where(wc => ids.Contains(wc.WorkerId))
                .Join(_context.Categories, wc => wc.CategoryId, c => c.CategoryId,
                    (wc, c) => new { wc.WorkerId, wc.IsPrimary, c.CategoryName })
                .OrderByDescending(x => x.IsPrimary).ThenBy(x => x.CategoryName).ToListAsync();
            var ratingRows = await _context.Reviews.Where(r => r.ReviewerRole == "Client" && r.Interview != null &&
                r.Interview.WorkerId != null && ids.Contains(r.Interview.WorkerId.Value))
                .Select(r => new { WorkerId = r.Interview!.WorkerId!.Value, Rating = r.Rating ?? 0m }).ToListAsync();
            var verifiedIds = (await _context.PoliceRecords.Where(pr => ids.Contains(pr.WorkerID) && !pr.IsRevoked && pr.ExpiryDate > DateTime.Now)
                .Select(pr => pr.WorkerID).Distinct().ToListAsync()).ToHashSet();
            var certifiedIds = (await _context.WorkerCertifications.Where(c => ids.Contains(c.WorkerID)).Select(c => c.WorkerID)
                .Union(_context.CourseCertificates.Where(c => ids.Contains(c.WorkerID) && !c.IsRevoked).Select(c => c.WorkerID))
                .ToListAsync()).ToHashSet();
            var exclusiveIds = (await _context.Hiring.Where(h => h.HiringDecision == "Accepted" && h.Interview != null &&
                h.Interview.WorkerId != null && ids.Contains(h.Interview.WorkerId.Value) && h.Interview.JobType == "Full-Time" &&
                h.Interview.IsExclusive && h.Interview.Status != "Resigned" && h.Interview.Status != "Terminated" &&
                h.Interview.Status != "Completed" && !_context.Resignations.Any(r => r.InterviewId == h.InterviewId))
                .Select(h => h.Interview!.WorkerId!.Value).Distinct().ToListAsync()).ToHashSet();

            var results = workers.Select(w =>
            {
                var cats = categoryRows.Where(c => c.WorkerId == w.WorkerId && !string.IsNullOrEmpty(c.CategoryName))
                    .Select(c => c.CategoryName).Distinct().ToList();
                var ratings = ratingRows.Where(r => r.WorkerId == w.WorkerId).Select(r => (double)r.Rating).ToList();
                return new WorkerCardVM
                {
                    Id = w.WorkerId,
                    Name = w.Name ?? "",
                    Role = cats.FirstOrDefault() ?? "General",
                    City = PublicLocality(w.Address),
                    Salary = w.Salary != null ? $"Rs.{w.Salary}" : "Not Set",
                    Phone = null,
                    Picture = w.Picture,
                    Rating = (ratings.Count > 0 ? Math.Round(ratings.Average(), 1) : 0).ToString("F1"),
                    Gender = w.Gender ?? "N/A",
                    Categories = cats,
                    AvailableStatus = (w.AvailableStatus ?? false) && !exclusiveIds.Contains(w.WorkerId),
                    IsPoliceVerified = verifiedIds.Contains(w.WorkerId),
                    IsCompanyCertified = certifiedIds.Contains(w.WorkerId)
                };
            }).ToList();
            ViewBag.Categories = await _context.Categories.OrderBy(c => c.CategoryName).Select(c => c.CategoryName).ToListAsync();
            ViewBag.Habits = await _context.Habits.Where(h => h.IsActive && h.IsWorkerSelectable && h.IsClientFilterable).OrderBy(h => h.SortOrder)
                .Select(h => new { h.HabitId, h.Name }).ToListAsync();
            ViewBag.Search = search;
            ViewBag.CurrentCity = currentCity ?? "All";
            ViewBag.AppliedCity = cityFilter ?? "All";
            return View(results);
        }

        private static string PublicLocality(string? address)
        {
            if (string.IsNullOrWhiteSpace(address)) return "Location not specified";
            var parts = address.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return parts.Length > 1 ? parts[^1] : "Locality shared after booking";
        }

        private async Task<string?> GetCurrentCityAsync()
        {
            var address = await _context.Clients.AsNoTracking()
                .Where(c => c.ClientId == ClientId)
                .Select(c => c.Address)
                .FirstOrDefaultAsync();
            return GeoHelper.CityFromAddress(address);
        }

        // ── FILTER SCREEN (separate page — RN FilterationScreen) ──────────────────
        // Fully client-side: the page builds the FindService query string on
        // Apply. Opens pre-filled with the currently applied filters so the
        // client can refine instead of starting over.
        [HttpGet]
        public async Task<IActionResult> Filter(string? gender, string? city, List<string>? categories,
            List<string>? subSkills, List<int>? habitIds)
        {
            ViewBag.Categories = await _context.Categories.OrderBy(c => c.CategoryName)
                .Select(c => new { c.CategoryId, c.CategoryName }).ToListAsync();
            ViewBag.Habits = await _context.Habits.Where(h => h.IsActive && h.IsWorkerSelectable && h.IsClientFilterable)
                .OrderBy(h => h.SortOrder).ThenBy(h => h.Name)
                .Select(h => new { h.HabitId, h.Name }).ToListAsync();
            var currentCity = await GetCurrentCityAsync();
            var cityOptions = new List<string> { "All" };
            if (!string.IsNullOrWhiteSpace(currentCity)) cityOptions.Add(currentCity);
            cityOptions.AddRange(new[]
            {
                "Islamabad", "Rawalpindi", "Lahore", "Karachi",
                "Faisalabad", "Peshawar", "Multan", "Quetta",
                "Gujranwala", "Sialkot"
            });
            ViewBag.Cities = cityOptions.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            ViewBag.Gender = gender ?? "";
            ViewBag.City = !string.IsNullOrWhiteSpace(city) ? city.Trim() : currentCity ?? "All";
            ViewBag.SelectedCategories = categories ?? new List<string>();
            ViewBag.SelectedSubSkills = subSkills ?? new List<string>();
            ViewBag.SelectedHabits = habitIds ?? new List<int>();
            return View();
        }

        // ── WORKER DETAIL ─────────────────────────────────────────────────────────
        public async Task<IActionResult> WorkerDetail(int id)
        {
            var worker = await _context.Workers
                .Include(w => w.Experiences)
                .FirstOrDefaultAsync(w => w.WorkerId == id);
            if (worker == null) return NotFound();

            var client = await _context.Clients.FindAsync(ClientId);

            // Reviews OF this worker, written BY clients (same filter as the
            // RN GetWorkerDetail payload) — reviewerName = the client's name.
            // Detail reviews: per-criterion breakdown + how the contract ended
            // (endType drives the Terminated/Resigned badge on the ReviewCard).
            var reviewRows = await _context.Reviews
                .Where(r => r.Interview != null && r.Interview.WorkerId == id && r.ReviewerRole == "Client")
                .OrderByDescending(r => r.ReviewDate)
                .Select(r => new
                {
                    r.ReviewId,
                    r.InterviewId,
                    Rating = r.Rating ?? 0m,
                    r.Comment,
                    r.ReviewDate,
                    EndType = r.Interview!.Status,
                    ReviewerName = r.Interview.Client != null ? r.Interview.Client.Name : "Anonymous",
                    ReviewerClientId = r.Interview.Client != null ? (int?)r.Interview.Client.ClientId : null,
                    Criteria = r.ReviewCriteriaRatings
                        .OrderBy(c => c.Criteria.SortOrder).ThenBy(c => c.CriteriaId)
                        .Select(c => new CriteriaScoreVM { Name = c.Criteria.CriteriaName, Score = c.Score })
                        .ToList()
                }).ToListAsync();

            // Contract window per review → the "Worked: …" line on each card.
            var reviewWindows = await ReviewHelpers.LoadContractWindowsAsync(_context,
                reviewRows.Where(x => x.InterviewId.HasValue).Select(x => x.InterviewId!.Value).Distinct().ToList(),
                reviewRows.Where(x => x.InterviewId.HasValue)
                    .GroupBy(x => x.InterviewId!.Value)
                    .ToDictionary(g => g.Key, g => g.Max(x => x.ReviewDate)));

            var reviews = reviewRows.Select(r => new ReviewVM
            {
                Rating = r.Rating,
                Comment = r.Comment,
                ReviewerRole = "Client",
                ReviewerName = r.ReviewerName ?? "Anonymous",
                ReviewDate = r.ReviewDate != null ? r.ReviewDate.Value.ToString("dd MMM yyyy") : "",
                WorkedPeriod = r.InterviewId.HasValue && reviewWindows.TryGetValue(r.InterviewId.Value, out var w)
                    ? ReviewHelpers.FormatWorkedPeriod(w.From, w.To) : null,
                EndType = r.EndType,
                Criteria = r.Criteria,
                ReviewerClientId = r.ReviewerClientId
            }).ToList();

            var catNames = await _context.WorkerCategories
                .Where(wc => wc.WorkerId == id)
                .Join(_context.Categories, wc => wc.CategoryId, c => c.CategoryId, (wc, c) => c.CategoryName)
                .Where(n => !string.IsNullOrEmpty(n)).Distinct().ToListAsync();

            var habits = await _context.WorkerHabits
                .Where(wh => wh.WorkerId == id && wh.Habit != null)
                .Select(wh => wh.Habit!.Name).ToListAsync();

            // Worker's published time slots (shown when this is a Part-Time engagement)
            var bookedSlotIds = (await _rules.ActiveSlotReservations(id).Select(i => i.SlotId!.Value).ToListAsync()).ToHashSet();
            var slots = await _context.WorkerTimeSlots
                .Where(s => s.WorkerId == id && s.IsActive)
                .OrderBy(s => s.StartTime)
                .Select(s => new SlotVM
                {
                    Id = s.Id,
                    StartTime = s.StartTime.ToString(@"hh\:mm"),
                    EndTime = s.EndTime.ToString(@"hh\:mm"),
                    IsTaken = bookedSlotIds.Contains(s.Id)
                }).ToListAsync();

            // Police character certificate (latest non-revoked) — ported from RN "Criminal Background Check"
            var policeCert = await _context.PoliceRecords
                .Where(pr => pr.WorkerID == id && !pr.IsRevoked)
                .OrderByDescending(pr => pr.IssuedDate)
                .FirstOrDefaultAsync();
            bool verified = policeCert != null && policeCert.ExpiryDate > DateTime.Now;

            // Company-issued skill/training certificates — ported from RN "Verified Training"
            var companyCerts = await _context.WorkerCertifications
                .Where(wc => wc.WorkerID == id)
                .OrderByDescending(wc => wc.IssuedDate)
                .Select(wc => new WorkerCertItemVM
                {
                    Title = wc.CertificateTitle,
                    Notes = wc.TrainingEvaluationNotes,
                    CompanyName = wc.Company != null ? wc.Company.CompanyName : "Training Provider",
                    IssuedDate = wc.IssuedDate.ToString("dd MMM yyyy")
                })
                .ToListAsync();

            // Verified Training card — port of GetWorkerCertificateDetail: the
            // legacy company certification first, else the newest non-revoked
            // course-completion certificate. One card, like the RN profile.
            string? trainingTitle = null, trainingCompany = null, trainingCode = null, trainingDate = null;
            bool trainingIsCourse = false;
            var legacyCert = companyCerts.FirstOrDefault();
            if (legacyCert != null)
            {
                trainingTitle = legacyCert.Title;
                trainingCompany = legacyCert.CompanyName;
                trainingDate = legacyCert.IssuedDate;
            }
            else
            {
                var courseCert = await _context.CourseCertificates
                    .Where(c => c.WorkerID == id && !c.IsRevoked)
                    .OrderByDescending(c => c.IssuedDateUtc)
                    .Select(c => new
                    {
                        c.CertificateCode,
                        c.IssuedDateUtc,
                        CourseName = c.Course.CourseName,
                        CompanyName = c.Company.CompanyName
                    })
                    .FirstOrDefaultAsync();
                if (courseCert != null)
                {
                    trainingTitle = courseCert.CourseName;
                    trainingCompany = courseCert.CompanyName;
                    trainingCode = courseCert.CertificateCode;
                    trainingDate = courseCert.IssuedDateUtc.ToString("dd MMM yyyy");
                    trainingIsCourse = true;
                }
            }

            // Fix: Check if THIS SPECIFIC CLIENT has an active or terminated/pending contract with this worker,
            // or if there is any active interview/hiring specifically between this ClientId and WorkerId.
            // Terminated status with THIS client blocks booking, but other clients can still hire.
            bool hasActive = await _context.Interviews.AnyAsync(i =>
                i.ClientId == ClientId && i.WorkerId == id &&
                (i.Status == "Pending" || i.Status == "Approved" || i.Status == "Finalized" || i.Status == "ResignationPending"));

            // Check if THIS client previously terminated this worker (if you want to permanently block this client from rehiring this specific worker)
            bool isTerminatedByThisClient = await _context.Interviews.AnyAsync(i =>
                i.ClientId == ClientId && i.WorkerId == id && i.Status == "Terminated");

            var hasExclusiveFullTime = await _rules.HasActiveExclusiveFullTimeJobAsync(id);
            var canViewContact = await _context.Interviews.AnyAsync(i => i.ClientId == ClientId && i.WorkerId == id &&
                (i.Status == "Finalized" || i.Status == "ResignationPending"));
            var canHire = !hasActive && !isTerminatedByThisClient;
            ViewBag.CanHire = canHire;
            ViewBag.ContractStatus = hasActive ? "You already have an active request or contract with this worker" :
                isTerminatedByThisClient ? "Rehiring is unavailable after you terminated this worker" :
                hasExclusiveFullTime ? "An exclusive full-time job is active; only a non-exclusive request can be considered" : "";

            string jobType = GeoHelper.ClassifyJobType(client, worker);
            double distanceKm = 0, radius = worker.Radius > 0 ? worker.Radius : 5.0;
            bool locationKnown = client != null && client.Latitude.HasValue && client.Longitude.HasValue &&
                                 worker.Latitude.HasValue && worker.Longitude.HasValue;
            if (locationKnown)
                distanceKm = GeoHelper.CalculateHaversineDistance(
                    Convert.ToDouble(client!.Latitude!.Value), Convert.ToDouble(client.Longitude!.Value),
                    Convert.ToDouble(worker.Latitude!.Value), Convert.ToDouble(worker.Longitude!.Value));

            var vm = new WorkerDetailVM
            {
                Id = worker.WorkerId,
                Name = worker.Name ?? "",
                Cnic = null,
                Phone = canViewContact ? worker.Phone : null,
                Address = PublicLocality(worker.Address),
                Picture = worker.Picture,
                Gender = worker.Gender,
                Age = worker.Age,
                Salary = worker.Salary != null ? "Rs." + worker.Salary.ToString() : "Not Set",
                Bio = worker.Bio,
                Rating = reviews.Any() ? Math.Round(reviews.Average(r => (double)r.Rating), 1) : 0.0,
                ReviewCount = reviews.Count,
                AvailableStatus = worker.AvailableStatus ?? false,
                IsPoliceVerified = verified,
                JobType = jobType,
                DistanceKm = Math.Round(distanceKm, 2),
                Radius = radius,
                LocationKnown = locationKnown,
                Categories = catNames,
                Experiences = worker.Experiences.Select(e => new ExperienceVM { WorkAt = e.WorkAt, ExpDetail = e.ExpDetail, Duration = e.Duration }).ToList(),
                Habits = habits,
                Slots = slots,
                Reviews = reviews,
                HasActiveInterview = hasActive,
                CanViewContact = canViewContact,
                CanHire = canHire,
                IsExclusiveFullTime = hasExclusiveFullTime,
                ContractStatus = ViewBag.ContractStatus,
                PoliceCertStatus = verified ? "Verified" : (policeCert != null ? "Expired" : "NotIssued"),
                PoliceCertNo = policeCert?.CertificateNo,
                PoliceCharacterStatus = policeCert?.CharacterStatus,
                PoliceIssuingStation = policeCert?.IssuingStation,
                PoliceIssuingBadge = policeCert?.IssuingBadge,
                PoliceIssuedDate = policeCert != null ? policeCert.IssuedDate.ToString("dd MMM yyyy") : null,
                PoliceExpiryDate = policeCert != null ? policeCert.ExpiryDate.ToString("dd MMM yyyy") : null,
                PoliceRemarks = policeCert?.Remarks,
                CompanyCertificates = companyCerts,
                HasVerifiedTraining = legacyCert != null || trainingIsCourse,
                TrainingTitle = trainingTitle,
                TrainingCompany = trainingCompany,
                TrainingIssuedDate = trainingDate,
                TrainingCode = trainingCode,
                TrainingIsCourse = trainingIsCourse
            };
            return View(vm);
        }

        // ── BOOK INTERVIEW ────────────────────────────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> BookInterview(int workerId, string? date = null, string? time = null, bool allowNonExclusive = false)
        {
            var worker = await _context.Workers.FindAsync(workerId);
            var client = await _context.Clients.FindAsync(ClientId);
            if (worker == null || client == null) return NotFound();
            var hasActive = await _context.Interviews.AnyAsync(i => i.ClientId == ClientId && i.WorkerId == workerId &&
                (i.Status == "Pending" || i.Status == "Approved" || i.Status == "Finalized" || i.Status == "ResignationPending"));
            var terminated = await _context.Interviews.AnyAsync(i => i.ClientId == ClientId && i.WorkerId == workerId && i.Status == "Terminated");
            if (hasActive || terminated)
            {
                TempData["Error"] = hasActive ? "You already have an active request or contract with this worker." : "This worker cannot be rehired after your termination.";
                return RedirectToAction("WorkerDetail", new { id = workerId });
            }
            var isPartTime = GeoHelper.ClassifyJobType(client, worker) == "Part-Time";
            double distanceKm = 0;
            if (client.Latitude.HasValue && client.Longitude.HasValue && worker.Latitude.HasValue && worker.Longitude.HasValue)
                distanceKm = GeoHelper.CalculateHaversineDistance(Convert.ToDouble(client.Latitude.Value), Convert.ToDouble(client.Longitude.Value),
                    Convert.ToDouble(worker.Latitude.Value), Convert.ToDouble(worker.Longitude.Value));
            DateTime day = DateTime.Today;
            if (!string.IsNullOrWhiteSpace(date) && DateTime.TryParse(date, out var parsed)) day = parsed.Date;
            var rawSlots = await _context.WorkerTimeSlots.Where(s => s.WorkerId == workerId && s.IsActive).OrderBy(s => s.StartTime).ToListAsync();
            var bookedSlotIds = (await _rules.ActiveSlotReservations(workerId).Select(i => i.SlotId!.Value).ToListAsync()).ToHashSet();
            var vm = new BookInterviewVM
            {
                WorkerId = workerId,
                WorkerName = worker.Name ?? "Worker",
                WorkerPicture = worker.Picture,
                IsPartTime = isPartTime,
                HasSlots = rawSlots.Any(s => !bookedSlotIds.Contains(s.Id)),
                Radius = worker.Radius > 0 ? worker.Radius : 5.0,
                DistanceKm = Math.Round(distanceKm, 2),
                ClientAddress = client.Address ?? "",
                Slots = rawSlots.Select(s => new SlotVM
                {
                    Id = s.Id,
                    StartTime = s.StartTime.ToString(@"hh\:mm"),
                    EndTime = s.EndTime.ToString(@"hh\:mm"),
                    IsTaken = bookedSlotIds.Contains(s.Id)
                }).ToList()
            };
            ViewBag.Date = day.ToString("yyyy-MM-dd");
            ViewBag.Time = TimeSpan.TryParse(time, out var pickedTime) ? pickedTime.ToString(@"hh\:mm") : DateTime.Now.ToString("HH:mm");
            ViewBag.AllowNonExclusive = allowNonExclusive;
            ViewBag.HasActiveFullTime = await _rules.HasActiveFullTimeJobAsync(workerId);
            return View(vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> BookInterview(int workerId, DateTime interviewDate, string? address,
            int? slotId, bool isResidenceProvided = false, string? interviewTime = null, bool isExclusive = false)
        {
            var client = await _context.Clients.FindAsync(ClientId);
            if (client == null) { TempData["Error"] = "Client profile not found."; return RedirectToAction("FindService"); }
            if (TimeSpan.TryParse(interviewTime, out var picked)) interviewDate = interviewDate.Date.Add(picked);
            var retryRoute = new
            {
                workerId,
                date = interviewDate == default ? DateTime.Today.ToString("yyyy-MM-dd") : interviewDate.ToString("yyyy-MM-dd"),
                time = interviewTime,
                allowNonExclusive = !isExclusive
            };
            if (interviewDate == default || interviewDate < DateTime.Now.AddMinutes(-1))
            { TempData["Error"] = "Choose a current or future interview date and time."; return RedirectToAction("BookInterview", retryRoute); }
            if (address?.Length > 1000)
            { TempData["Error"] = "Address must be 1,000 characters or fewer."; return RedirectToAction("BookInterview", retryRoute); }
            if (await _context.Interviews.AnyAsync(i => i.ClientId == ClientId && i.WorkerId == workerId && i.Status == "Terminated"))
            { TempData["Error"] = "You cannot rehire a worker whose contract you terminated."; return RedirectToAction("WorkerDetail", new { id = workerId }); }
            if (await _context.Interviews.AnyAsync(i => i.ClientId == ClientId && i.WorkerId == workerId &&
                (i.Status == "Pending" || i.Status == "Approved" || i.Status == "Finalized" || i.Status == "ResignationPending")))
            { TempData["Error"] = "You already have an active request or contract with this worker."; return RedirectToAction("WorkerDetail", new { id = workerId }); }

            await using var transaction = await _context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
            var worker = await _rules.LockWorkerAsync(workerId);
            if (worker == null) { TempData["Error"] = "Worker not found."; return RedirectToAction("FindService"); }
            var model = new Interview
            {
                ClientId = ClientId,
                WorkerId = workerId,
                InterviewDate = interviewDate,
                Address = string.IsNullOrWhiteSpace(address) ? client.Address : address.Trim(),
                Status = "Pending",
                WorkerDecision = "Pending",
                JobType = GeoHelper.ClassifyJobType(client, worker)
            };
            model.IsExclusive = model.JobType == "Full-Time" && isExclusive;
            if (model.IsExclusive && await _rules.HasActiveFullTimeJobAsync(workerId))
            {
                TempData["Error"] = "This worker already has an active full-time job. You may proceed with a non-exclusive request, but final confirmation must wait until that job ends.";
                return RedirectToAction("BookInterview", new { workerId, date = interviewDate.ToString("yyyy-MM-dd"), time = interviewTime, allowNonExclusive = true });
            }
            if (model.JobType == "Part-Time")
            {
                model.IsResidenceProvided = false; model.IsExclusive = false;
                if (slotId == null) { TempData["Error"] = "Please select an available time slot."; return RedirectToAction("BookInterview", retryRoute); }
                var chosen = await _context.WorkerTimeSlots.FirstOrDefaultAsync(s => s.Id == slotId.Value && s.WorkerId == workerId);
                if (chosen == null || !chosen.IsActive)
                { TempData["Error"] = "That slot is no longer available. Refresh and choose another."; return RedirectToAction("BookInterview", retryRoute); }
                var matchingSlotIds = await _context.WorkerTimeSlots.Where(s => s.WorkerId == workerId &&
                    s.StartTime == chosen.StartTime && s.EndTime == chosen.EndTime).Select(s => s.Id).ToListAsync();
                var slotTaken = await _context.Interviews.AnyAsync(i => i.WorkerId == workerId && i.SlotId != null &&
                    matchingSlotIds.Contains(i.SlotId.Value) && i.Status != "Rejected" && i.Status != "JobRejected" &&
                    i.Status != "Terminated" && i.Status != "Resigned" && i.Status != "ResignationPending" && i.Status != "Completed");
                if (slotTaken)
                { TempData["Error"] = "That recurring slot is reserved by another active interview, regardless of its displayed date."; return RedirectToAction("BookInterview", retryRoute); }
                model.SlotId = slotId;
            }
            else { model.SlotId = null; model.IsResidenceProvided = isResidenceProvided; }
            _context.Interviews.Add(model);
            await _context.SaveChangesAsync();
            await transaction.CommitAsync();
            TempData["Success"] = $"{model.JobType} interview request sent to {worker.Name}!";
            return RedirectToAction("Dashboard");
        }

        // ── ACTIVE REQUESTS (workers who accepted the interview) ──────────────────
        public async Task<IActionResult> ActiveRequests()
        {
            int clientId = ClientId;
            var accepted = await _context.Interviews
                .Include(i => i.Worker)
                .Where(i => i.ClientId == clientId && i.WorkerDecision == "Accepted" && i.Status == "Pending")
                .Select(i => new InterviewRequestVM
                {
                    InterviewId = i.InterviewId,
                    OtherPartyId = i.WorkerId ?? 0,
                    OtherPartyName = i.Worker != null ? i.Worker.Name ?? "Worker" : "Worker",
                    OtherPartyPicture = i.Worker != null ? i.Worker.Picture : null,
                    Location = i.Address ?? "N/A",
                    Time = i.InterviewDate != null ? i.InterviewDate.Value.ToString("MMM dd, hh:mm tt") : "Not Set",
                    Status = i.Status ?? "",
                    WorkerDecision = i.WorkerDecision ?? "",
                    JobType = i.JobType ?? "Full-Time",
                    IsExclusive = i.IsExclusive && i.JobType == "Full-Time"
                }).ToListAsync();
            return View(accepted);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateHiring(int interviewId, string? address)
        {
            if (address?.Length > 1000) { TempData["Error"] = "Address must be 1,000 characters or fewer."; return RedirectToAction("ActiveRequests"); }
            await using var transaction = await _context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
            var interview = await _workflow.LockInterviewAsync(interviewId);
            if (interview == null || interview.ClientId != ClientId) { TempData["Error"] = "Interview not found."; return RedirectToAction("ActiveRequests"); }
            if (interview.Status != "Pending" || interview.WorkerDecision != "Accepted")
            { TempData["Error"] = "Only an interview accepted by the worker can become a job offer."; return RedirectToAction("ActiveRequests"); }
            if (await _context.Hiring.AnyAsync(h => h.InterviewId == interviewId))
            { TempData["Error"] = "A job offer already exists for this interview."; return RedirectToAction("ActiveRequests"); }
            interview.Status = "Approved";
            _context.Hiring.Add(new Hiring
            {
                InterviewId = interviewId,
                WorkerDecision = "Pending",
                HiringDecision = "Pending",
                Address = string.IsNullOrWhiteSpace(address) ? interview.Address : address.Trim(),
                HiringDate = DateTime.Now
            });
            await _context.SaveChangesAsync(); await transaction.CommitAsync();
            TempData["Success"] = "Interview approved. Job offer sent to the worker.";
            return RedirectToAction("ActiveRequests");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> WithdrawRequest(int interviewId)
        {
            await using var transaction = await _context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
            var interview = await _workflow.LockInterviewAsync(interviewId);
            if (interview == null || interview.ClientId != ClientId) { TempData["Error"] = "Request not found."; return RedirectToAction("ActiveRequests"); }
            if (interview.Status != "Pending" || await _context.Hiring.AnyAsync(h => h.InterviewId == interviewId))
            { TempData["Error"] = "Only a pending request without a job offer can be withdrawn."; return RedirectToAction("ActiveRequests"); }
            interview.Status = "Rejected"; interview.WorkerDecision = "Rejected";
            await _context.SaveChangesAsync(); await transaction.CommitAsync();
            TempData["Success"] = "Request withdrawn.";
            return RedirectToAction("ActiveRequests");
        }

        // ── WORKER DECISIONS (finalize hiring once worker accepts the offer) ──────
        public async Task<IActionResult> WorkerDecisions()
        {
            int clientId = ClientId;
            var list = await _context.Hiring
                .Include(h => h.Interview).ThenInclude(i => i!.Worker)
                .Where(h => h.Interview!.ClientId == clientId)
                .Select(h => new InterviewRequestVM
                {
                    InterviewId = h.InterviewId ?? 0,
                    HiringId = h.HiringId,
                    OtherPartyId = h.Interview!.WorkerId ?? 0,
                    OtherPartyName = h.Interview.Worker != null ? h.Interview.Worker.Name ?? "Worker" : "Worker",
                    OtherPartyPicture = h.Interview.Worker != null ? h.Interview.Worker.Picture : null,
                    Location = h.Address ?? "N/A",
                    JobType = h.Interview.JobType ?? "Full-Time",
                    IsExclusive = h.Interview.IsExclusive && h.Interview.JobType == "Full-Time",
                    Status = h.Interview.Status ?? "",
                    HiringWorkerDecision = h.WorkerDecision ?? "Pending",
                    HiringDecision = h.HiringDecision ?? "Pending"
                }).ToListAsync();
            return View(list);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> FinalizeHiring(int hiringId)
        {
            await using var transaction = await _context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
            var interviewId = await _context.Hiring.AsNoTracking().Where(h => h.HiringId == hiringId)
                .Select(h => h.InterviewId).FirstOrDefaultAsync();
            if (interviewId == null) { TempData["Error"] = "Hiring record not found."; return RedirectToAction("WorkerDecisions"); }
            var interview = await _workflow.LockInterviewAsync(interviewId.Value);
            if (interview == null || interview.ClientId != ClientId)
            { TempData["Error"] = "Hiring record not found."; return RedirectToAction("WorkerDecisions"); }
            var hiring = await _context.Hiring.FirstOrDefaultAsync(h => h.HiringId == hiringId && h.InterviewId == interviewId.Value);
            if (hiring == null) { TempData["Error"] = "Hiring record not found."; return RedirectToAction("WorkerDecisions"); }
            if (hiring.WorkerDecision != "Accepted" || hiring.HiringDecision != "Pending" || interview.Status != "Approved")
            { TempData["Error"] = "This offer is not awaiting final client confirmation."; return RedirectToAction("WorkerDecisions"); }
            if (interview.JobType == "Full-Time" && interview.WorkerId.HasValue)
            {
                if (await _rules.LockWorkerAsync(interview.WorkerId.Value) == null)
                { TempData["Error"] = "Worker not found."; return RedirectToAction("WorkerDecisions"); }
                if (await _rules.HasActiveFullTimeJobAsync(interview.WorkerId.Value, interview.InterviewId))
                { TempData["Error"] = "This worker already has an active full-time job. They must leave it before this offer can be confirmed."; return RedirectToAction("WorkerDecisions"); }
            }
            hiring.HiringDecision = "Accepted"; hiring.HiringDate = DateTime.Now; interview.Status = "Finalized";
            if (interview.JobType == "Full-Time" && interview.IsExclusive && interview.WorkerId.HasValue)
            {
                var worker = await _context.Workers.FindAsync(interview.WorkerId.Value);
                if (worker != null) worker.AvailableStatus = false;
            }
            await _workflow.SetPartTimeSlotActiveAsync(interview, false);
            await _context.SaveChangesAsync(); await transaction.CommitAsync();
            TempData["Success"] = "Worker hired successfully!";
            return RedirectToAction("WorkerDecisions");
        }

        // ── RESIGNATIONS ──────────────────────────────────────────────────────────
        public async Task<IActionResult> Resignations()
        {
            int clientId = ClientId;
            await _workflow.ProcessDueAsync(clientId: clientId);
            var list = await _context.Resignations
                .Include(r => r.Interview).ThenInclude(i => i!.Worker)
                .Where(r => r.Interview!.ClientId == clientId)
                .OrderByDescending(r => r.SubmittedDate)
                .Select(r => new ResignationVM
                {
                    ResignationId = r.ResignationId,
                    InterviewId = r.InterviewId ?? 0,
                    WorkerName = r.Interview!.Worker != null ? r.Interview.Worker.Name ?? "Worker" : "Worker",
                    WorkerAvatar = r.Interview.Worker != null ? r.Interview.Worker.Picture : null,
                    Reason = r.ResignationReason,
                    LastWorkingDate = r.LastWorkingDate.ToString("dd MMM yyyy"),
                    SubmittedDate = r.SubmittedDate != null ? r.SubmittedDate.Value.ToString("dd MMM yyyy") : "",
                    Status = r.Interview.Status ?? ""
                }).ToListAsync();
            return View(list);
        }

        public async Task<IActionResult> ResignationDetail(int id)
        {
            await _workflow.ProcessDueAsync(clientId: ClientId);
            var r = await _context.Resignations
                .Include(x => x.Interview).ThenInclude(i => i!.Worker)
                .FirstOrDefaultAsync(x => x.ResignationId == id && x.Interview != null && x.Interview.ClientId == ClientId);
            if (r == null) return NotFound();

            var interview = r.Interview;
            bool isConfirmed = ResignationWorkflowService.IsAccepted(r);
            int interviewId = r.InterviewId ?? 0;

            // Both closing reviews for this contract (RN GetResignationDetail):
            // the worker's own review written when resigning, and the client's
            // criteria-scored review written when confirming.
            var reviewRows = await _context.Reviews
                .Where(x => x.InterviewId == interviewId)
                .OrderByDescending(x => x.ReviewDate)
                .Select(x => new
                {
                    x.ReviewId,
                    x.ReviewerRole,
                    Rating = x.Rating ?? 0m,
                    x.Comment,
                    x.ReviewDate,
                    Criteria = x.ReviewCriteriaRatings
                        .OrderBy(c => c.Criteria.SortOrder).ThenBy(c => c.CriteriaId)
                        .Select(c => new CriteriaScoreVM { Name = c.Criteria.CriteriaName, Score = c.Score })
                        .ToList()
                }).ToListAsync();

            // Contract window: hire date → last working / submitted date.
            var windows = await ReviewHelpers.LoadContractWindowsAsync(_context,
                new List<int> { interviewId },
                reviewRows.GroupBy(x => interviewId).ToDictionary(g => g.Key, g => g.Max(x => x.ReviewDate)));

            ReviewVM? MapReview(string role, string fallbackName)
            {
                var row = reviewRows.FirstOrDefault(x => x.ReviewerRole == role);
                if (row == null) return null;
                windows.TryGetValue(interviewId, out var w);
                return new ReviewVM
                {
                    Rating = row.Rating,
                    Comment = row.Comment,
                    ReviewerRole = role,
                    ReviewerName = fallbackName,
                    ReviewDate = row.ReviewDate != null ? row.ReviewDate.Value.ToString("dd MMM yyyy") : "",
                    WorkedPeriod = ReviewHelpers.FormatWorkedPeriod(w.From, w.To),
                    EndType = interview?.Status,
                    Criteria = row.Criteria
                };
            }

            var workerReview = MapReview("Worker", interview?.Worker?.Name ?? "Worker");
            var clientReview = MapReview("Client", "You");
            var vm = new ResignationVM
            {
                ResignationId = r.ResignationId,
                InterviewId = interviewId,
                WorkerName = interview?.Worker?.Name ?? "Worker",
                WorkerAvatar = interview?.Worker?.Picture,
                Reason = r.ResignationReason,
                LastWorkingDate = r.LastWorkingDate.ToString("dd MMM yyyy"),
                SubmittedDate = r.SubmittedDate != null ? r.SubmittedDate.Value.ToString("dd MMM yyyy") : "",
                Status = interview?.Status ?? "",
                IsConfirmed = isConfirmed,
                AcceptanceMode = r.AcceptanceMode,
                AcceptedAt = r.AcceptedAtUtc?.ToString("dd MMM yyyy HH:mm 'UTC'"),
                WorkerReview = workerReview,
                ClientReview = clientReview,
                CriteriaGroups = clientReview == null ? await LoadCriteriaGroupsAsync(interview?.WorkerId ?? 0) : new List<CriteriaGroupVM>()
            };
            return View(vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ConfirmResignation(int interviewId, int? resignationId, string? comment,
            List<CriteriaScoreInput>? criteriaScores, int? rating)
        {
            try
            {
                var notice = await _workflow.ConfirmWithClientReviewAsync(ClientId, interviewId, resignationId, async () =>
                {
                    if (string.IsNullOrWhiteSpace(comment) || comment.Trim().Length > 4000)
                        throw new ResignationWorkflowException(400, "Review remarks are required and must be at most 4,000 characters.");
                    var criteria = await BuildCriteriaRatingsAsync(interviewId, criteriaScores);
                    if (criteria.Error != null) throw new ResignationWorkflowException(400, criteria.Error);
                    return new Review { Rating = criteria.Average, Comment = comment?.Trim(), ReviewCriteriaRatings = criteria.Rows };
                });
                TempData["Success"] = notice.AcceptanceMode == "Automatic" ? "Your closing review has been saved." : "Resignation accepted and your closing review was saved.";
                return RedirectToAction("ResignationDetail", new { id = notice.ResignationId });
            }
            catch (ResignationWorkflowException ex)
            {
                TempData["Error"] = ex.Message;
                return resignationId.HasValue ? RedirectToAction("ResignationDetail", new { id = resignationId.Value }) : RedirectToAction("Resignations");
            }
        }

        // ── CRITERIA-BASED CLOSING REVIEWS (api Dashboard GetReviewCriteria) ─────
        // The profession a review is about is derived, never stored:
        //   Interview -> Worker -> Worker_Category -> Category -> ReviewCriteria
        // Star rows for the client's input: the common set ("Basics") plus one
        // group per profession the worker holds.
        private async Task<List<CriteriaGroupVM>> LoadCriteriaGroupsAsync(int workerId)
        {
            var categoryIds = await _context.WorkerCategories
                .Where(wc => wc.WorkerId == workerId)
                .Select(wc => wc.CategoryId)
                .Distinct()
                .ToListAsync();

            var criteria = await _context.ReviewCriteria
                .Where(c => c.IsActive && (c.CategoryId == null || categoryIds.Contains(c.CategoryId!.Value)))
                .OrderBy(c => c.CategoryId)   // NULLs first in SQL Server => common criteria lead
                .ThenBy(c => c.SortOrder)
                .ThenBy(c => c.CriteriaId)
                .Select(c => new
                {
                    c.CriteriaId,
                    c.CategoryId,
                    CategoryName = c.Category != null ? c.Category.CategoryName : null,
                    c.CriteriaName
                })
                .ToListAsync();

            var groups = new List<CriteriaGroupVM>();

            var common = criteria.Where(c => c.CategoryId == null).ToList();
            if (common.Count > 0)
                groups.Add(new CriteriaGroupVM
                {
                    GroupName = "Basics",
                    Items = common.Select(c => new CriteriaInputVM { CriteriaId = c.CriteriaId, Name = c.CriteriaName }).ToList()
                });

            foreach (var g in criteria.Where(c => c.CategoryId != null).GroupBy(c => new { c.CategoryId, c.CategoryName }))
            {
                groups.Add(new CriteriaGroupVM
                {
                    GroupName = g.Key.CategoryName ?? "Profession",
                    Items = g.Select(c => new CriteriaInputVM { CriteriaId = c.CriteriaId, Name = c.CriteriaName }).ToList()
                });
            }
            return groups;
        }

        /// <summary>
        /// Validates the submitted scores against the worker's applicable criteria
        /// (all mandatory, nothing extra, each 1-5) and returns the rows to persist
        /// plus the overall average that goes into Reviews.Rating.
        /// </summary>
        private async Task<(string? Error, decimal Average, List<ReviewCriteriaRating> Rows)> BuildCriteriaRatingsAsync(
            int? interviewId, List<CriteriaScoreInput>? submitted)
        {
            var workerId = await _context.Interviews
                .Where(i => i.InterviewId == interviewId)
                .Select(i => i.WorkerId)
                .FirstOrDefaultAsync();

            if (workerId == null)
                return ("Interview or its worker could not be found.", 0m, new List<ReviewCriteriaRating>());

            var applicable = await _context.ReviewCriteria
                .Where(c => c.IsActive && (c.CategoryId == null ||
                    _context.WorkerCategories.Any(wc => wc.WorkerId == workerId && wc.CategoryId == c.CategoryId)))
                .Select(c => c.CriteriaId)
                .ToListAsync();
            var requiredIds = applicable.ToHashSet();

            submitted ??= new List<CriteriaScoreInput>();
            var submittedIds = submitted.Select(s => s.CriteriaId).ToHashSet();

            if (submitted.Count == 0)
                return ("No criteria scores were submitted.", 0m, new List<ReviewCriteriaRating>());

            var missing = requiredIds.Except(submittedIds).ToList();
            if (missing.Count > 0)
                return ("Please rate every criterion. Missing criteria: " + string.Join(", ", missing), 0m, new List<ReviewCriteriaRating>());

            var unexpected = submittedIds.Except(requiredIds).ToList();
            if (unexpected.Count > 0)
                return ("Criteria that do not apply to this worker were submitted: " + string.Join(", ", unexpected), 0m, new List<ReviewCriteriaRating>());

            if (submittedIds.Count != submitted.Count)
                return ("The same criterion was scored more than once.", 0m, new List<ReviewCriteriaRating>());

            if (submitted.Any(s => s.Score < 1 || s.Score > 5))
                return ("Each criterion must be scored between 1 and 5.", 0m, new List<ReviewCriteriaRating>());

            var rows = submitted
                .Select(s => new ReviewCriteriaRating { CriteriaId = s.CriteriaId, Score = s.Score })
                .ToList();

            // Overall = mean of the criteria scores, rounded to 2dp to fit decimal(3,2).
            decimal average = Math.Round(submitted.Average(s => (decimal)s.Score), 2);
            return (null, average, rows);
        }

        // ── TERMINATE CONTRACT (RN TerminateContractScreen — criteria review) ────
        [HttpGet]
        public async Task<IActionResult> Terminate(int interviewId)
        {
            var interview = await _context.Interviews.Include(i => i.Worker)
                .FirstOrDefaultAsync(i => i.InterviewId == interviewId && i.ClientId == ClientId);
            if (interview == null) return NotFound();
            var canTerminate = !ContractRules.IsEnded(interview.Status) && await _context.Hiring.AnyAsync(h =>
                h.InterviewId == interviewId && h.HiringDecision == "Accepted");
            if (!canTerminate) { TempData["Error"] = "Only an active hired contract can be terminated."; return RedirectToAction("Dashboard"); }
            ViewBag.InterviewId = interviewId; ViewBag.WorkerName = interview.Worker?.Name ?? "Worker";
            ViewBag.WorkerPicture = interview.Worker?.Picture;
            ViewBag.CriteriaGroups = await LoadCriteriaGroupsAsync(interview.WorkerId ?? 0);
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Terminate(int interviewId, string reason, string? remarks,
            List<CriteriaScoreInput>? criteriaScores, int? rating)
        {
            if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 4000 || remarks?.Length > 4000)
            { TempData["Error"] = "A termination reason of at most 4,000 characters is required; remarks have the same limit."; return RedirectToAction("Terminate", new { interviewId }); }
            await using var transaction = await _context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
            var interview = await _workflow.LockInterviewAsync(interviewId);
            if (interview == null || interview.ClientId != ClientId)
            { TempData["Error"] = "Job record not found."; return RedirectToAction("Dashboard"); }
            if (ContractRules.IsEnded(interview.Status) || !await _context.Hiring.AnyAsync(h => h.InterviewId == interviewId && h.HiringDecision == "Accepted"))
            { TempData["Error"] = "This contract is not active."; return RedirectToAction("Dashboard"); }
            if (await _context.Terminations.AnyAsync(t => t.InterviewId == interviewId))
            { TempData["Error"] = "A termination already exists for this contract."; return RedirectToAction("Dashboard"); }

            var alreadyReviewed = await _context.Reviews.AnyAsync(r => r.InterviewId == interviewId && r.ReviewerRole == "Client");
            if (alreadyReviewed)
            { TempData["Error"] = "A closing review already exists for this contract."; return RedirectToAction("Dashboard"); }
            {
                var criteria = await BuildCriteriaRatingsAsync(interviewId, criteriaScores);
                if (criteria.Error != null)
                { TempData["Error"] = criteria.Error; return RedirectToAction("Terminate", new { interviewId }); }
                _context.Reviews.Add(new Review
                {
                    InterviewId = interviewId,
                    Rating = criteria.Average,
                    Comment = remarks?.Trim(),
                    ReviewerRole = "Client",
                    ReviewDate = ResignationWorkflowService.BusinessLocal(DateTime.UtcNow),
                    ReviewCriteriaRatings = criteria.Rows
                });
            }
            interview.Status = "Terminated";
            _context.Terminations.Add(new Termination
            {
                InterviewId = interviewId,
                TerminatedDate = DateOnly.FromDateTime(DateTime.Now),
                TerminatedReason = reason.Trim()
            });
            await _workflow.SetPartTimeSlotActiveAsync(interview, true);
            await _rules.ReleaseExclusiveAvailabilityAsync(interview);
            await _context.SaveChangesAsync(); await transaction.CommitAsync();
            TempData["Success"] = "Contract terminated successfully.";
            return RedirectToAction("Dashboard");
        }

        // ── PROFILE ───────────────────────────────────────────────────────────────
        public async Task<IActionResult> Profile()
        {
            var client = await _context.Clients.FindAsync(ClientId);
            if (client == null) return NotFound();

            // Reviews OF this client, written BY workers — now with the detail-review
            // fields (criteria + endType) exactly like the RN client profile.
            var workerReviewRows = await _context.Reviews
                .Where(r => r.Interview != null && r.Interview.ClientId == ClientId && r.ReviewerRole == "Worker")
                .OrderByDescending(r => r.ReviewDate)
                .Select(r => new
                {
                    Rating = r.Rating ?? 0m,
                    r.Comment,
                    r.ReviewDate,
                    EndType = r.Interview!.Status,
                    ReviewerName = r.Interview.Worker != null ? r.Interview.Worker.Name : "Anonymous",
                    ReviewerPicture = r.Interview.Worker != null ? r.Interview.Worker.Picture : null,
                    Criteria = r.ReviewCriteriaRatings
                        .OrderBy(c => c.Criteria.SortOrder).ThenBy(c => c.CriteriaId)
                        .Select(c => new CriteriaScoreVM { Name = c.Criteria.CriteriaName, Score = c.Score })
                        .ToList()
                }).ToListAsync();

            ViewBag.Reviews = workerReviewRows.Select(r => new ReviewVM
            {
                Rating = r.Rating,
                Comment = r.Comment,
                ReviewerRole = "Worker",
                ReviewerName = r.ReviewerName ?? "Anonymous",
                ReviewDate = r.ReviewDate != null ? r.ReviewDate.Value.ToString("dd MMM yyyy") : "",
                EndType = r.EndType,
                Criteria = r.Criteria
            }).ToList();

            return View(client);
        }

        // ── EDIT PROFILE ───────────────────────────────────────────────────────────
        // Ported from the RN flow UserDashboardScreen → Signup (isEdit: true) →
        // POST api/AccountCreation/UpdateClient. Validations mirror the RN form:
        // name/phone/address/email required, photo + password optional in edit
        // mode, passwords must match. The API never changes Latitude/Longitude
        // on update (location is managed on the map screen), so neither do we.
        [HttpGet]
        public async Task<IActionResult> EditProfile()
        {
            var client = await _context.Clients.FindAsync(ClientId);
            if (client == null) return NotFound();

            return View(new EditClientProfileVM
            {
                Name = client.Name,
                Email = client.Email,
                Phone = client.Phone,
                Address = client.Address,
                CurrentPicture = client.Picture
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditProfile(EditClientProfileVM model)
        {
            if (string.IsNullOrWhiteSpace(model.Name) || !AccountController.ValidPhone(model.Phone) ||
                string.IsNullOrWhiteSpace(model.Address) || !AccountController.ValidEmail(model.Email))
                ModelState.AddModelError(string.Empty, "Enter a name, valid phone, address, and valid email address.");
            if (!string.IsNullOrEmpty(model.Password) && model.Password != "********" &&
                (!PasswordService.IsValidNewPassword(model.Password) || model.Password != model.ConfirmPassword))
                ModelState.AddModelError(string.Empty, "New passwords must match and contain 8–100 characters.");
            var existing = await _context.Clients.FindAsync(ClientId);
            if (existing == null) return NotFound();
            var email = model.Email?.Trim().ToLowerInvariant() ?? "";
            if (await _context.Clients.AnyAsync(c => c.ClientId != ClientId && c.Email.ToLower() == email))
                ModelState.AddModelError(nameof(model.Email), "That email is already registered.");
            if (!ModelState.IsValid) { model.CurrentPicture = existing.Picture; return View(model); }
            try
            {
                if (model.PictureFile != null)
                {
                    var imagePath = await _images.SaveImageAsync(model.PictureFile);
                    if (imagePath == "Invalid")
                    { ModelState.AddModelError(string.Empty, "Choose a valid JPG or PNG image up to 5 MB."); model.CurrentPicture = existing.Picture; return View(model); }
                    if (imagePath != null) existing.Picture = imagePath;
                }
                existing.Name = model.Name!.Trim(); existing.Phone = model.Phone!.Trim(); existing.Address = model.Address!.Trim(); existing.Email = email;
                if (!string.IsNullOrEmpty(model.Password) && model.Password != "********") existing.Password = _passwords.Hash(model.Password!);
                await _context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Client {ClientId} profile update failed", ClientId);
                ModelState.AddModelError(string.Empty, "Profile update could not be completed. Please try again.");
                model.CurrentPicture = existing.Picture; return View(model);
            }
            await RefreshSignInAsync(existing.ClientId, existing.Name, existing.Picture, existing.Email, existing.Password);
            TempData["Success"] = "Profile updated successfully!";
            return RedirectToAction("Profile");
        }

        // Re-issues the auth cookie after a profile edit so the sidebar shows
        // the new name/photo without a re-login (same claims as Account/Login).
        private async Task RefreshSignInAsync(int clientId, string name, string? picture, string email, string? storedPassword)
        {
            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, clientId.ToString()),
                new(ClaimTypes.Name, string.IsNullOrWhiteSpace(name) ? "Client" : name),
                new(ClaimTypes.Role, "Client"),
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

        // ── MAP / LOCATION SCREEN ───────────────────────────────────────────────────
        // Ported from the RN FindServiceScreen "Map" button → MapScreen:
        // a Leaflet map where the client drops a pin (their location) among
        // nearby worker markers, then saves via UpdateLocation.
        public async Task<IActionResult> Location()
        {
            var client = await _context.Clients.FindAsync(ClientId);
            if (client == null) return NotFound();

            var workers = await _rules.DiscoverableByClient(_context.Workers.AsNoTracking(), ClientId)
                .Where(w => w.Latitude.HasValue && w.Longitude.HasValue)
                .Select(w => new
                {
                    w.WorkerId,
                    w.Name,
                    w.Latitude,
                    w.Longitude,
                    Role = _context.WorkerCategories
                        .Where(wc => wc.WorkerId == w.WorkerId).OrderByDescending(wc => wc.IsPrimary)
                        .Join(_context.Categories, wc => wc.CategoryId, c => c.CategoryId, (wc, c) => c.CategoryName)
                        .FirstOrDefault()
                })
                .ToListAsync();

            ViewBag.Workers = workers.Select(w => new MapWorkerVM
            {
                Id = w.WorkerId,
                Name = w.Name ?? "Worker",
                Role = string.IsNullOrWhiteSpace(w.Role) ? "Service Worker" : w.Role!,
                // Approximate public areas (~1–2 km), never exact worker home pins.
                Lat = Math.Round(Convert.ToDouble(w.Latitude!.Value), 2),
                Lng = Math.Round(Convert.ToDouble(w.Longitude!.Value), 2)
            }).ToList();

            return View(client);
        }

        // ── LOCATION (AJAX) ───────────────────────────────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateLocation([FromBody] LatLng dto)
        {
            if (dto == null || !AccountController.ValidCoordinates(dto.Latitude, dto.Longitude))
                return Json(new { ok = false, message = "Enter valid latitude and longitude coordinates." });
            var client = await _context.Clients.FindAsync(ClientId);
            if (client == null) return Json(new { ok = false, message = "Client not found." });
            client.Latitude = dto.Latitude;
            client.Longitude = dto.Longitude;
            await _context.SaveChangesAsync();
            return Json(new { ok = true });
        }

        public class LatLng { public double Latitude { get; set; } public double Longitude { get; set; } }
    }
}