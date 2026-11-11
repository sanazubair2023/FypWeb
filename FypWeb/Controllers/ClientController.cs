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
                .OrderByDescending(h => h.HiringId) // HiringDate ki jaga HiringId se sort kar diya taake error na aaye
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
                    hiringDate = h.HiringDate
                })
                .ToListAsync();

            var interviewIds = hired.Where(h => h.interviewId.HasValue).Select(h => h.interviewId!.Value).ToList();

            var resignationMap = await _context.Resignations
                .Where(r => r.InterviewId.HasValue && interviewIds.Contains(r.InterviewId.Value))
                .GroupBy(r => r.InterviewId!.Value)
                .Select(g => new {
                    InterviewId = g.Key,
                    ResignationId = g.OrderByDescending(r => r.SubmittedDate)
                                      .ThenByDescending(r => r.ResignationId)
                                      .Select(r => (int?)r.ResignationId)
                                      .FirstOrDefault()
                })
                .ToDictionaryAsync(x => x.InterviewId, x => x.ResignationId);

            var selectedSkillsMap = await ReviewHelpers.LoadSelectedSkillsLookupAsync(
                _context,
                interviewIds);

            var cards = hired.Select(h =>
            {
                var iid = h.interviewId ?? 0;
                selectedSkillsMap.TryGetValue(iid, out var skills);
                resignationMap.TryGetValue(iid, out int? resignationId);

                return new InterviewRequestVM
                {
                    InterviewId = iid,
                    OtherPartyId = h.workerId ?? 0,
                    OtherPartyName = h.name ?? "Worker",
                    OtherPartyPicture = h.picture,
                    Location = h.location ?? "N/A",
                    JobType = h.jobType,
                    SlotStart = h.slotStart.HasValue ? DateTime.Today.Add(h.slotStart.Value).ToString("hh:mm tt") : null,
                    SlotEnd = h.slotEnd.HasValue ? DateTime.Today.Add(h.slotEnd.Value).ToString("hh:mm tt") : null,
                    Status = h.status switch
                    {
                        "ResignationPending" => "Pending Resignation",
                        "Resigned" => "Resigned",
                        "Terminated" => "Terminated",
                        "Completed" => "Completed",
                        _ => "On Work"
                    },
                    IsExclusive = h.isExclusive,
                    ResignationId = resignationId,
                    HiringDate = h.hiringDate?.ToString("dd MMM yyyy"),
                    SelectedSkills = skills ?? new List<string>()
                };
            }).ToList();

            ViewBag.HiredCount = cards.Count(c => c.Status is "On Work" or "Pending Resignation");
            return View(cards);
        }
        // ── FIND SERVICE (search + category pills + filters) ──────────────────────
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
                    Salary = w.Salary != null ? $"Rs.{w.Salary:0}" : "Not Set",
                    Phone = w.Phone,
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
            ViewBag.SelectedCategories = categories ?? new List<string>();
            ViewBag.SelectedGender = gender ?? "";
            ViewBag.SelectedSubSkills = subSkills ?? new List<string>();
            ViewBag.SelectedHabits = habitIds ?? new List<int>();
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
            return parts.Length > 1 ? parts[^1] : parts[0];
        }

        private async Task<string?> GetCurrentCityAsync()
        {
            var address = await _context.Clients.AsNoTracking()
                .Where(c => c.ClientId == ClientId)
                .Select(c => c.Address)
                .FirstOrDefaultAsync();
            return GeoHelper.CityFromAddress(address);
        }

        // ── FILTER SCREEN (RN FilterationScreen) ──────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> Filter(string? gender, string? city, List<string>? categories,
            List<string>? subSkills, List<int>? habitIds)
        {
            ViewBag.Categories = await _context.Categories.OrderBy(c => c.CategoryName)
                .Select(c => new { c.CategoryId, c.CategoryName }).ToListAsync();
            ViewBag.Habits = await _context.Habits.Where(h => h.IsActive && h.IsWorkerSelectable && h.IsClientFilterable)
                .OrderBy(h => h.SortOrder).ThenBy(h => h.Name)
                .Select(h => new { h.HabitId, h.Name, h.ExclusiveGroup }).ToListAsync();
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

        // ── WORKER DETAIL (RN WorkerDetailScreen) ─────────────────────────────────
        public async Task<IActionResult> WorkerDetail(int id)
        {
            var worker = await _context.Workers
                .Include(w => w.Experiences)
                .FirstOrDefaultAsync(w => w.WorkerId == id);
            if (worker == null) return NotFound();

            var client = await _context.Clients.FindAsync(ClientId);
            var reviews = await LoadWorkerClientReviewsAsync(id);
            var skillSummary = await ReviewHelpers.LoadWorkerSkillBreakdownAsync(_context, id);

            var habits = await _context.WorkerHabits
                .Where(wh => wh.WorkerId == id && wh.Habit != null && wh.Habit.IsActive)
                .OrderBy(wh => wh.Habit!.SortOrder)
                .ThenBy(wh => wh.Habit!.Name)
                .Select(wh => wh.Habit!.Name).ToListAsync();

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

            var policeCert = await _context.PoliceRecords
                .Where(pr => pr.WorkerID == id && !pr.IsRevoked)
                .OrderByDescending(pr => pr.IssuedDate)
                .FirstOrDefaultAsync();
            bool verified = policeCert != null && policeCert.ExpiryDate > DateTime.Now;

            var allCerts = await ReviewHelpers.LoadWorkerCertificatesAsync(_context, id);
            var companyCerts = allCerts
                .Where(c => c.CertificateType == "CompanyCertification")
                .Select(c => new WorkerCertItemVM
                {
                    Title = c.CertificateTitle,
                    Notes = c.EvaluationNotes,
                    CompanyName = c.CompanyName,
                    IssuedDate = c.IssuedDate
                }).ToList();

            var featuredCert = allCerts.FirstOrDefault(c => c.CertificateType == "CompanyCertification")
                            ?? allCerts.FirstOrDefault();

            // Match RN behavior 1:1: previously terminated workers CAN be re-booked;
            // only an active in-flight interview/contract blocks a duplicate request.
            bool hasActive = await _context.Interviews.AnyAsync(i =>
                i.ClientId == ClientId && i.WorkerId == id &&
                (i.Status == "Pending" || i.Status == "Approved" || i.Status == "Finalized" || i.Status == "ResignationPending"));

            var hasExclusiveFullTime = await _rules.HasActiveExclusiveFullTimeJobAsync(id);
            var canHire = !hasActive;
            ViewBag.CanHire = canHire;
            ViewBag.ContractStatus = hasActive
                ? "You already have an active request or contract with this worker"
                : hasExclusiveFullTime
                    ? "An exclusive full-time job is active; only a non-exclusive request can be considered"
                    : "";

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
                Cnic = worker.Cnic,
                Phone = worker.Phone,
                Address = worker.Address ?? PublicLocality(worker.Address),
                Picture = worker.Picture,
                Gender = worker.Gender,
                Age = worker.Age,
                Salary = worker.Salary != null ? $"Rs.{worker.Salary:0}" : "Not Set",
                Bio = worker.Bio,
                Rating = reviews.Any() ? Math.Round(reviews.Average(r => (double)r.Rating), 1) : 0.0,
                ReviewCount = reviews.Count,
                AvailableStatus = worker.AvailableStatus ?? false,
                IsPoliceVerified = verified,
                JobType = jobType,
                DistanceKm = Math.Round(distanceKm, 2),
                Radius = radius,
                LocationKnown = locationKnown,
                Categories = skillSummary.Categories,
                PrimaryRole = skillSummary.PrimaryRole,
                PrimaryCategoryAverageRating = skillSummary.PrimaryCategoryAverageRating,
                PrimaryCategoryRatingCount = skillSummary.PrimaryCategoryRatingCount,
                PrimarySkillRatings = skillSummary.PrimarySkillRatings,
                PartTimeSkills = skillSummary.PartTimeSkills,
                Experiences = worker.Experiences.Select(e => new ExperienceVM { WorkAt = e.WorkAt, ExpDetail = e.ExpDetail, Duration = e.Duration }).ToList(),
                Habits = habits,
                Slots = slots,
                Reviews = reviews,
                HasActiveInterview = hasActive,
                CanViewContact = !string.IsNullOrWhiteSpace(worker.Phone),
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
                HasVerifiedTraining = featuredCert != null,
                TotalCertificateCount = allCerts.Count,
                TrainingTitle = featuredCert?.CertificateTitle,
                TrainingCompany = featuredCert?.CompanyName,
                TrainingIssuedDate = featuredCert?.IssuedDate,
                TrainingCode = featuredCert?.CertificateCode,
                TrainingIsCourse = featuredCert?.CertificateType == "CourseCompletion"
            };
            return View(vm);
        }

        private async Task<List<ReviewVM>> LoadWorkerClientReviewsAsync(int workerId)
        {
            var reviewRows = await _context.Reviews.AsNoTracking()
                .Where(r => r.Interview != null && r.Interview.WorkerId == workerId && r.ReviewerRole == "Client")
                .OrderByDescending(r => r.ReviewDate)
                .ThenByDescending(r => r.ReviewId)
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
                        .ToList(),
                    SkillRatings = r.ReviewSkillRatings
                        .OrderBy(s => s.Skill.SkillName).ThenBy(s => s.SkillsId)
                        .Select(s => new CriteriaScoreVM { Name = s.Skill.SkillName ?? "Skill", Score = s.Score })
                        .ToList()
                }).ToListAsync();

            var reviewWindows = await ReviewHelpers.LoadContractWindowsAsync(_context,
                reviewRows.Where(x => x.InterviewId.HasValue).Select(x => x.InterviewId!.Value).Distinct().ToList(),
                reviewRows.Where(x => x.InterviewId.HasValue)
                    .GroupBy(x => x.InterviewId!.Value)
                    .ToDictionary(g => g.Key, g => g.Max(x => x.ReviewDate)));

            return reviewRows.Select(r =>
            {
                var hasWin = reviewWindows.TryGetValue(r.InterviewId ?? 0, out var w);
                return new ReviewVM
                {
                    Rating = r.Rating,
                    Comment = r.Comment,
                    ReviewerRole = "Client",
                    ReviewerName = r.ReviewerName ?? "Anonymous",
                    ReviewDate = r.ReviewDate != null ? r.ReviewDate.Value.ToString("dd MMM yyyy") : "",
                    WorkedPeriod = hasWin ? ReviewHelpers.FormatWorkedPeriod(w.From, w.To) : null,
                    EndType = r.EndType,
                    EndReasonType = hasWin ? w.EndReasonType : null,
                    EndReason = hasWin ? w.EndReason : null,
                    Criteria = r.Criteria,
                    SkillRatings = r.SkillRatings,
                    ReviewerClientId = r.ReviewerClientId
                };
            }).ToList();
        }

        // ── READ-ONLY POLICE CHARACTER CERTIFICATE (RN CharacterCertificateScreen) ──
        [HttpGet]
        public async Task<IActionResult> CharacterCertificate(int id)
        {
            var worker = await _context.Workers.AsNoTracking()
                .Where(w => w.WorkerId == id)
                .Select(w => new { w.WorkerId, Name = w.Name ?? "Worker", Cnic = w.Cnic ?? "", w.Picture })
                .FirstOrDefaultAsync();
            if (worker == null) return NotFound();

            var cert = await _context.PoliceRecords.AsNoTracking()
                .Where(pr => pr.WorkerID == id && !pr.IsRevoked)
                .OrderByDescending(pr => pr.IssuedDate)
                .FirstOrDefaultAsync();

            bool valid = cert != null && cert.ExpiryDate > DateTime.Now;
            var vm = new CharacterCertificateVM
            {
                WorkerId = worker.WorkerId,
                Name = worker.Name,
                Cnic = worker.Cnic,
                Picture = worker.Picture,
                HasValidCertificate = valid,
                Status = valid ? "Verified" : (cert != null ? "Expired" : "NotIssued"),
                CertificateNo = cert?.CertificateNo,
                CharacterStatus = cert?.CharacterStatus,
                Remarks = cert?.Remarks,
                IssuingStation = cert?.IssuingStation,
                IssuingBadge = cert?.IssuingBadge,
                IssuedDate = cert != null ? cert.IssuedDate.ToString("dd MMM yyyy") : null,
                ExpiryDate = cert != null ? cert.ExpiryDate.ToString("dd MMM yyyy") : null,
                DaysUntilExpiry = cert != null ? (int)Math.Ceiling((cert.ExpiryDate.Date - DateTime.Now.Date).TotalDays) : null,
                ViewerRole = "Client"
            };
            return View("~/Views/Shared/CharacterCertificate.cshtml", vm);
        }

        // ── VERIFIED CERTIFICATES LIST & RECORD DETAIL (RN WorkerCertificationDetail) ──
        [HttpGet]
        public async Task<IActionResult> WorkerCertificates(int id)
        {
            var worker = await _context.Workers.AsNoTracking()
                .Where(w => w.WorkerId == id)
                .Select(w => new { w.WorkerId, Name = w.Name ?? "Worker", w.Picture })
                .FirstOrDefaultAsync();
            if (worker == null) return NotFound();

            var vm = new WorkerCertificateListVM
            {
                WorkerId = worker.WorkerId,
                WorkerName = worker.Name,
                WorkerPicture = worker.Picture,
                ViewerRole = "Client",
                Certificates = await ReviewHelpers.LoadWorkerCertificatesAsync(_context, id)
            };
            return View("~/Views/Shared/WorkerCertificates.cshtml", vm);
        }

        [HttpGet]
        public async Task<IActionResult> WorkerCertificateRecord(int workerId, string type, int certId)
        {
            var all = await ReviewHelpers.LoadWorkerCertificatesAsync(_context, workerId);
            var record = all.FirstOrDefault(c =>
                c.CertificateId == certId &&
                string.Equals(c.CertificateType, type, StringComparison.OrdinalIgnoreCase));
            if (record == null) return NotFound();
            ViewBag.ViewerRole = "Client";
            return View("~/Views/Shared/WorkerCertificateRecord.cshtml", record);
        }

        // ── STANDALONE WORKER RATINGS & REVIEWS (RN RatingAndReviewsScreen) ────────
        [HttpGet]
        public async Task<IActionResult> WorkerReviews(int id)
        {
            var worker = await _context.Workers.AsNoTracking()
                .Where(w => w.WorkerId == id)
                .Select(w => new { w.WorkerId, Name = w.Name ?? "Worker", w.Picture })
                .FirstOrDefaultAsync();
            if (worker == null) return NotFound();

            var reviews = await LoadWorkerClientReviewsAsync(id);
            var skillSummary = await ReviewHelpers.LoadWorkerSkillBreakdownAsync(_context, id);

            var vm = new RatingsAndReviewsPageVM
            {
                WorkerId = worker.WorkerId,
                WorkerName = worker.Name,
                WorkerPicture = worker.Picture,
                WorkerRole = skillSummary.PrimaryRole,
                AverageRating = reviews.Any() ? Math.Round(reviews.Average(r => (double)r.Rating), 1) : 0.0,
                ReviewCount = reviews.Count,
                ViewerRole = "Client",
                Reviews = reviews
            };
            return View("~/Views/Shared/RatingsAndReviews.cshtml", vm);
        }

        // ── CONTRACT EXIT SUMMARY (RN WorkerTerminatedScreen) ─────────────────────
        [HttpGet]
        public async Task<IActionResult> ContractSummary(int workerId, int? interviewId = null)
        {
            var query = _context.Interviews.AsNoTracking()
                .Include(i => i.Client)
                .Include(i => i.Worker)
                .Where(i => i.ClientId == ClientId && i.WorkerId == workerId &&
                            (i.Status == "Terminated" || i.Status == "Resigned"));
            if (interviewId.HasValue)
                query = query.Where(i => i.InterviewId == interviewId.Value);

            var interview = await query
                .OrderByDescending(i => i.InterviewId)
                .FirstOrDefaultAsync();
            if (interview == null) return NotFound();

            string reason = "No details specified";
            string dateStr = "N/A";

            if (interview.Status == "Terminated")
            {
                var term = await _context.Terminations.AsNoTracking()
                    .Where(t => t.InterviewId == interview.InterviewId)
                    .OrderByDescending(t => t.TerminatedDate)
                    .FirstOrDefaultAsync();
                if (term != null)
                {
                    reason = term.TerminatedReason ?? "No reason specified";
                    if (term.TerminatedDate.HasValue)
                        dateStr = term.TerminatedDate.Value.ToString("dd MMM yyyy");
                }
            }
            else if (interview.Status == "Resigned")
            {
                var res = await _context.Resignations.AsNoTracking()
                    .Where(r => r.InterviewId == interview.InterviewId)
                    .OrderByDescending(r => r.SubmittedDate)
                    .FirstOrDefaultAsync();
                if (res != null)
                {
                    reason = res.ResignationReason ?? "No reason specified";
                    dateStr = res.AcceptedAtUtc?.ToString("dd MMM yyyy")
                           ?? res.SubmittedDate?.ToString("dd MMM yyyy")
                           ?? "N/A";
                }
            }

            var workerSkill = await _context.WorkerCategories.AsNoTracking()
                .Where(wc => wc.WorkerId == workerId)
                .OrderByDescending(wc => wc.IsPrimary)
                .Join(_context.Categories, wc => wc.CategoryId, c => c.CategoryId, (wc, c) => c.CategoryName)
                .FirstOrDefaultAsync() ?? "Worker";

            var vm = new ContractExitSummaryVM
            {
                InterviewId = interview.InterviewId,
                WorkerId = workerId,
                Status = interview.Status ?? "Terminated",
                Date = dateStr,
                Reason = reason,
                WorkerName = interview.Worker?.Name ?? "Worker",
                WorkerPicture = interview.Worker?.Picture,
                WorkerPhone = interview.Worker?.Phone ?? "N/A",
                WorkerAddress = interview.Worker?.Address ?? "N/A",
                WorkerSkill = workerSkill,
                ClientName = interview.Client?.Name ?? "Client",
                ClientPicture = interview.Client?.Picture,
                ClientAddress = interview.Client?.Address ?? "N/A",
                ViewerRole = "Client"
            };
            return View("~/Views/Shared/ContractSummary.cshtml", vm);
        }

        // ── OFFERED SKILLS HELPER (RN GetSkillsForInterview) ──────────────────────
        private async Task<List<OfferedSkillCategoryGroupVM>> LoadOfferedSkillGroupsAsync(int workerId)
        {
            var rows = await (
                from wc in _context.WorkerCategories.AsNoTracking()
                join skill in _context.Skills.AsNoTracking()
                    on wc.SkillsId equals skill.SkillsId
                join cat in _context.Categories.AsNoTracking()
                    on wc.CategoryId equals cat.CategoryId
                where wc.WorkerId == workerId && skill.CategoryId == wc.CategoryId
                orderby wc.IsPrimary descending, cat.CategoryId, skill.SkillName, skill.SkillsId
                select new
                {
                    cat.CategoryId,
                    CategoryName = cat.CategoryName ?? "Service",
                    skill.SkillsId,
                    SkillName = skill.SkillName ?? "Skill"
                }).ToListAsync();

            return rows
                .GroupBy(r => new { r.CategoryId, r.CategoryName })
                .Select(g => new OfferedSkillCategoryGroupVM
                {
                    CategoryId = g.Key.CategoryId,
                    CategoryName = g.Key.CategoryName,
                    Skills = g.GroupBy(x => x.SkillsId)
                        .Select(sg => new OfferedSkillItemVM
                        {
                            SkillsId = sg.Key,
                            SkillName = sg.First().SkillName
                        }).ToList()
                }).ToList();
        }

        [HttpGet]
        public async Task<IActionResult> GetSkillsForInterview(int workerId)
        {
            if (workerId <= 0) return BadRequest(new { message = "A valid workerId is required." });
            if (!await _context.Workers.AnyAsync(w => w.WorkerId == workerId))
                return NotFound(new { message = "Worker not found." });

            var groups = await LoadOfferedSkillGroupsAsync(workerId);
            var flat = groups.SelectMany(g => g.Skills.Select(s => new
            {
                categoryId = g.CategoryId,
                categoryName = g.CategoryName,
                skillsId = s.SkillsId,
                skillName = s.SkillName
            })).ToList();
            return Json(flat);
        }

        // ── BOOK INTERVIEW (RN InterviewSelectionScreen + SelectInterviewSkills) ──
        [HttpGet]
        public async Task<IActionResult> BookInterview(int workerId, string? date = null, string? time = null, bool allowNonExclusive = false)
        {
            var worker = await _context.Workers.FindAsync(workerId);
            var client = await _context.Clients.FindAsync(ClientId);
            if (worker == null || client == null) return NotFound();

            var hasActive = await _context.Interviews.AnyAsync(i => i.ClientId == ClientId && i.WorkerId == workerId &&
                (i.Status == "Pending" || i.Status == "Approved" || i.Status == "Finalized" || i.Status == "ResignationPending"));
            if (hasActive)
            {
                TempData["Error"] = "You already have an active request or contract with this worker.";
                return RedirectToAction("WorkerDetail", new { id = workerId });
            }

            var vm = await BuildBookInterviewVMAsync(worker, client, new List<int>(), null, false, false, false);
            DateTime day = DateTime.Today;
            if (!string.IsNullOrWhiteSpace(date) && DateTime.TryParse(date, out var parsed)) day = parsed.Date;
            ViewBag.Date = day.ToString("yyyy-MM-dd");
            ViewBag.Time = TimeSpan.TryParse(time, out var pickedTime) ? pickedTime.ToString(@"hh\:mm") : DateTime.Now.AddHours(1).ToString("HH:mm");
            ViewBag.AllowNonExclusive = allowNonExclusive;
            ViewBag.HasActiveFullTime = vm.HasActiveFullTimeJob;
            return View(vm);
        }

        private async Task<BookInterviewVM> BuildBookInterviewVMAsync(
            Worker worker, Client client, List<int> selectedSkillIds, int? selectedSlotId,
            bool isResidenceProvided, bool isExclusive, bool conflictActiveFullTime)
        {
            var isPartTime = GeoHelper.ClassifyJobType(client, worker) == "Part-Time";
            double distanceKm = 0;
            if (client.Latitude.HasValue && client.Longitude.HasValue && worker.Latitude.HasValue && worker.Longitude.HasValue)
                distanceKm = GeoHelper.CalculateHaversineDistance(
                    Convert.ToDouble(client.Latitude.Value), Convert.ToDouble(client.Longitude.Value),
                    Convert.ToDouble(worker.Latitude.Value), Convert.ToDouble(worker.Longitude.Value));

            var rawSlots = await _context.WorkerTimeSlots.Where(s => s.WorkerId == worker.WorkerId && s.IsActive).OrderBy(s => s.StartTime).ToListAsync();
            var bookedSlotIds = (await _rules.ActiveSlotReservations(worker.WorkerId).Select(i => i.SlotId!.Value).ToListAsync()).ToHashSet();
            var offeredGroups = await LoadOfferedSkillGroupsAsync(worker.WorkerId);
            var hasActiveFt = await _rules.HasActiveFullTimeJobAsync(worker.WorkerId);

            return new BookInterviewVM
            {
                WorkerId = worker.WorkerId,
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
                }).ToList(),
                OfferedSkillGroups = offeredGroups,
                SelectedSkillIds = selectedSkillIds,
                SelectedSlotId = selectedSlotId,
                IsResidenceProvided = isResidenceProvided,
                IsExclusive = isExclusive,
                HasActiveFullTimeJob = hasActiveFt,
                ConflictActiveFullTime = conflictActiveFullTime
            };
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> BookInterview(int workerId, DateTime interviewDate, string? address,
            int? slotId, List<int>? selectedSkillIds, bool isResidenceProvided = false,
            string? interviewTime = null, bool isExclusive = false, bool forceNonExclusive = false)
        {
            var client = await _context.Clients.FindAsync(ClientId);
            if (client == null) { TempData["Error"] = "Client profile not found."; return RedirectToAction("FindService"); }
            var workerLookup = await _context.Workers.FindAsync(workerId);
            if (workerLookup == null) { TempData["Error"] = "Worker not found."; return RedirectToAction("FindService"); }

            if (TimeSpan.TryParse(interviewTime, out var picked))
                interviewDate = interviewDate.Date.Add(picked);

            var cleanSkills = (selectedSkillIds ?? new List<int>()).Where(id => id > 0).Distinct().ToList();

            async Task<IActionResult> RerenderWithErrorAsync(string? errorMsg, bool conflictFt = false)
            {
                if (!string.IsNullOrEmpty(errorMsg)) TempData["Error"] = errorMsg;
                var vm = await BuildBookInterviewVMAsync(workerLookup, client, cleanSkills, slotId, isResidenceProvided, isExclusive, conflictFt);
                if (!string.IsNullOrWhiteSpace(address)) vm.ClientAddress = address;
                ViewBag.Date = interviewDate == default ? DateTime.Today.ToString("yyyy-MM-dd") : interviewDate.ToString("yyyy-MM-dd");
                ViewBag.Time = !string.IsNullOrWhiteSpace(interviewTime) ? interviewTime : DateTime.Now.AddHours(1).ToString("HH:mm");
                ViewBag.AllowNonExclusive = conflictFt || forceNonExclusive || !isExclusive;
                ViewBag.HasActiveFullTime = vm.HasActiveFullTimeJob;
                return View("BookInterview", vm);
            }

            if (cleanSkills.Count == 0)
                return await RerenderWithErrorAsync("Select at least one skill offered by this worker before booking.");

            var offeredSkillIds = await (
                from wc in _context.WorkerCategories.AsNoTracking()
                join s in _context.Skills.AsNoTracking() on wc.SkillsId equals s.SkillsId
                where wc.WorkerId == workerId && wc.CategoryId == s.CategoryId && cleanSkills.Contains(s.SkillsId)
                select s.SkillsId).Distinct().ToListAsync();

            if (offeredSkillIds.Count != cleanSkills.Count)
                return await RerenderWithErrorAsync("One or more selected skills are not offered by this worker. Please review your selection.");

            if (interviewDate == default || interviewDate < DateTime.Now.AddMinutes(-5))
                return await RerenderWithErrorAsync("Choose a current or future interview date and time.");
            if (address?.Length > 1000)
                return await RerenderWithErrorAsync("Address must be 1,000 characters or fewer.");

            if (await _context.Interviews.AnyAsync(i => i.ClientId == ClientId && i.WorkerId == workerId &&
                (i.Status == "Pending" || i.Status == "Approved" || i.Status == "Finalized" || i.Status == "ResignationPending")))
            {
                TempData["Error"] = "You already have an active request or contract with this worker.";
                return RedirectToAction("WorkerDetail", new { id = workerId });
            }

            var classifiedJobType = GeoHelper.ClassifyJobType(client, workerLookup);
            var effectiveExclusive = classifiedJobType == "Full-Time" && isExclusive && !forceNonExclusive;

            // RN 409 ACTIVE_FULL_TIME_JOB conflict modal parity: prompt client to proceed non-exclusive or find another worker
            if (effectiveExclusive && await _rules.HasActiveFullTimeJobAsync(workerId))
                return await RerenderWithErrorAsync(null, conflictFt: true);

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
            model.IsExclusive = model.JobType == "Full-Time" && isExclusive && !forceNonExclusive;
            if (model.IsExclusive && await _rules.HasActiveFullTimeJobAsync(workerId))
            {
                await transaction.RollbackAsync();
                return await RerenderWithErrorAsync(null, conflictFt: true);
            }

            var matchingSlotIds = new List<int>();
            if (model.JobType == "Part-Time")
            {
                model.IsResidenceProvided = false;
                model.IsExclusive = false;
                if (slotId == null)
                {
                    await transaction.RollbackAsync();
                    return await RerenderWithErrorAsync("Please select one of the worker's available time slots.");
                }
                var chosen = await _context.WorkerTimeSlots.FirstOrDefaultAsync(s => s.Id == slotId.Value && s.WorkerId == workerId);
                if (chosen == null || !chosen.IsActive)
                {
                    await transaction.RollbackAsync();
                    return await RerenderWithErrorAsync("That time slot is no longer available. Please choose another slot.");
                }
                matchingSlotIds = await _context.WorkerTimeSlots
                    .Where(s => s.WorkerId == workerId && s.StartTime == chosen.StartTime && s.EndTime == chosen.EndTime)
                    .Select(s => s.Id).ToListAsync();

                var slotTaken = await _context.Interviews.AnyAsync(i => i.WorkerId == workerId && i.SlotId != null &&
                    matchingSlotIds.Contains(i.SlotId.Value) && i.Status != "Rejected" && i.Status != "JobRejected" &&
                    i.Status != "Terminated" && i.Status != "Resigned" && i.Status != "ResignationPending" && i.Status != "Completed");
                if (slotTaken)
                {
                    await transaction.RollbackAsync();
                    return await RerenderWithErrorAsync("That time slot is already booked for another interview. Please pick another slot.");
                }
                model.SlotId = slotId;
            }
            else
            {
                model.SlotId = null;
                model.IsResidenceProvided = isResidenceProvided;
            }

            _context.Interviews.Add(model);
            await _context.SaveChangesAsync();

            _context.InterviewSkills.AddRange(cleanSkills.Select(sid => new InterviewSkill
            {
                InterviewId = model.InterviewId,
                SkillsId = sid
            }));
            await _context.SaveChangesAsync();

            if (model.JobType == "Part-Time" && model.SlotId != null)
            {
                bool lostRace = await _context.Interviews.AnyAsync(i =>
                    i.InterviewId != model.InterviewId &&
                    i.InterviewId < model.InterviewId &&
                    i.WorkerId == model.WorkerId &&
                    i.SlotId.HasValue && matchingSlotIds.Contains(i.SlotId.Value) &&
                    i.Status != "Rejected" && i.Status != "JobRejected" &&
                    i.Status != "Terminated" && i.Status != "Resigned" &&
                    i.Status != "ResignationPending" && i.Status != "Completed");

                if (lostRace)
                {
                    await transaction.RollbackAsync();
                    return await RerenderWithErrorAsync("That time slot was just booked by another client. Please choose another slot.");
                }
            }

            await transaction.CommitAsync();
            TempData["Success"] = $"{model.JobType} interview request sent to {worker.Name}!";
            return RedirectToAction("ActiveRequests");
        }

        // ── INTERVIEW LIST / ACTIVE REQUESTS (RN ActiveRequestScreen: All | Pending | Approved + Search) ──
        public async Task<IActionResult> ActiveRequests(string tab = "all", string? search = null)
        {
            int clientId = ClientId;
            tab = (tab ?? "all").Trim().ToLowerInvariant();
            if (tab is not ("all" or "pending" or "approved")) tab = "all";

            var rows = await _context.Interviews.AsNoTracking()
                .Include(i => i.Worker)
                .Include(i => i.Slot)
                .Where(i => i.ClientId == clientId)
                .OrderByDescending(i => i.InterviewId)
                .Select(i => new
                {
                    i.InterviewId,
                    WorkerId = i.WorkerId ?? 0,
                    WorkerName = i.Worker != null ? i.Worker.Name ?? "Worker" : "Worker",
                    WorkerPic = i.Worker != null ? i.Worker.Picture : null,
                    CategoryName = _context.WorkerCategories
                        .Where(wc => wc.WorkerId == i.WorkerId)
                        .OrderByDescending(wc => wc.IsPrimary)
                        .Join(_context.Categories, wc => wc.CategoryId, c => c.CategoryId, (wc, c) => c.CategoryName)
                        .FirstOrDefault() ?? "General Worker",
                    i.Address,
                    i.InterviewDate,
                    Status = i.Status ?? "Pending",
                    WorkerDecision = i.WorkerDecision ?? "Pending",
                    JobType = i.JobType ?? "Full-Time",
                    i.IsResidenceProvided,
                    IsExclusive = i.IsExclusive && i.JobType == "Full-Time",
                    SlotStart = i.Slot != null ? (TimeSpan?)i.Slot.StartTime : null,
                    SlotEnd = i.Slot != null ? (TimeSpan?)i.Slot.EndTime : null,
                    HasHiring = _context.Hiring.Any(h => h.InterviewId == i.InterviewId)
                }).ToListAsync();

            var skillsMap = await ReviewHelpers.LoadSelectedSkillsLookupAsync(
                _context, rows.Select(r => r.InterviewId));

            var allItems = rows.Select(r =>
            {
                skillsMap.TryGetValue(r.InterviewId, out var skills);
                skills ??= new List<string>();

                string badgeLabel;
                string badgeClass;
                string statusHint;
                string tabGroup;
                bool canApprove = false;
                bool canDelete = false;

                if (r.Status == "Pending" && r.WorkerDecision == "Accepted")
                {
                    badgeLabel = "Awaiting Your Approval";
                    badgeClass = "chip-green";
                    statusHint = "Worker accepted the interview — click Approve to send a job offer.";
                    tabGroup = "pending";
                    canApprove = !r.HasHiring;
                    canDelete = !r.HasHiring;
                }
                else if (r.Status == "Pending" && r.WorkerDecision == "Pending")
                {
                    badgeLabel = "Pending";
                    badgeClass = "chip-amber";
                    statusHint = "Waiting for the worker to accept or decline your interview request.";
                    tabGroup = "pending";
                    canDelete = !r.HasHiring;
                }
                else if (r.Status == "Approved")
                {
                    badgeLabel = "Approved · Offer Sent";
                    badgeClass = "chip-blue";
                    statusHint = "Job offer sent — track final acceptance in Worker Decisions.";
                    tabGroup = "approved";
                }
                else if (r.Status == "Finalized")
                {
                    badgeLabel = "Accepted · Hired";
                    badgeClass = "chip-green";
                    statusHint = "Hiring handshake finalized — active contract.";
                    tabGroup = "approved";
                }
                else if (r.Status == "ResignationPending")
                {
                    badgeLabel = "Notice Period";
                    badgeClass = "chip-amber";
                    statusHint = "Worker submitted a 7-day resignation notice.";
                    tabGroup = "approved";
                }
                else if (r.Status == "Resigned")
                {
                    badgeLabel = "Resigned";
                    badgeClass = "chip-gray";
                    statusHint = "Contract ended via resignation.";
                    tabGroup = "other";
                }
                else if (r.Status == "Terminated")
                {
                    badgeLabel = "Terminated";
                    badgeClass = "chip-red";
                    statusHint = "Contract terminated by client.";
                    tabGroup = "other";
                }
                else
                {
                    badgeLabel = "Interview Rejected";
                    badgeClass = "chip-red";
                    statusHint = "This request was declined or withdrawn.";
                    tabGroup = "other";
                    canDelete = !r.HasHiring;
                }

                return new InterviewRequestVM
                {
                    InterviewId = r.InterviewId,
                    OtherPartyId = r.WorkerId,
                    OtherPartyName = r.WorkerName,
                    OtherPartyPicture = r.WorkerPic,
                    CategoryName = r.CategoryName,
                    Location = r.Address ?? "N/A",
                    Time = r.InterviewDate != null ? r.InterviewDate.Value.ToString("dd MMM yyyy, hh:mm tt") : "Not Set",
                    Status = r.Status,
                    WorkerDecision = r.WorkerDecision,
                    JobType = r.JobType,
                    IsResidenceProvided = r.IsResidenceProvided,
                    IsExclusive = r.IsExclusive,
                    SlotStart = r.SlotStart.HasValue ? DateTime.Today.Add(r.SlotStart.Value).ToString("hh:mm tt") : null,
                    SlotEnd = r.SlotEnd.HasValue ? DateTime.Today.Add(r.SlotEnd.Value).ToString("hh:mm tt") : null,
                    SelectedSkills = skills,
                    TabGroup = tabGroup,
                    BadgeLabel = badgeLabel,
                    BadgeClass = badgeClass,
                    StatusHint = statusHint,
                    CanApprove = canApprove,
                    CanDelete = canDelete
                };
            }).ToList();

            if (!string.IsNullOrWhiteSpace(search))
            {
                var q = search.Trim();
                allItems = allItems.Where(item =>
                    item.OtherPartyName.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                    item.CategoryName.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                    item.SelectedSkills.Any(s => s.Contains(q, StringComparison.OrdinalIgnoreCase))).ToList();
            }

            ViewBag.AllCount = allItems.Count;
            ViewBag.PendingTabCount = allItems.Count(x => x.TabGroup == "pending");
            ViewBag.ApprovedTabCount = allItems.Count(x => x.TabGroup == "approved");
            ViewBag.ActiveTab = tab;
            ViewBag.Search = search;

            var filtered = tab switch
            {
                "pending" => allItems.Where(x => x.TabGroup == "pending").ToList(),
                "approved" => allItems.Where(x => x.TabGroup == "approved").ToList(),
                _ => allItems
            };

            return View(filtered);
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
            await _context.SaveChangesAsync();
            await transaction.CommitAsync();
            TempData["Success"] = "Interview approved. Job offer sent to the worker.";
            return RedirectToAction("ActiveRequests", new { tab = "approved" });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> WithdrawRequest(int interviewId)
        {
            return await DeleteInterview(interviewId);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteInterview(int interviewId)
        {
            await using var transaction = await _context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
            var interview = await _context.Interviews.FirstOrDefaultAsync(i => i.InterviewId == interviewId && i.ClientId == ClientId);
            if (interview == null)
            {
                TempData["Error"] = "Interview request not found.";
                return RedirectToAction("ActiveRequests");
            }
            if (await _context.Hiring.AnyAsync(h => h.InterviewId == interviewId) ||
                await _context.Reviews.AnyAsync(r => r.InterviewId == interviewId) ||
                (interview.Status != "Pending" && interview.Status != "Rejected"))
            {
                TempData["Error"] = "Only pending or rejected requests without an active job offer can be deleted.";
                return RedirectToAction("ActiveRequests");
            }

            _context.Interviews.Remove(interview);
            await _context.SaveChangesAsync();
            await transaction.CommitAsync();
            TempData["Success"] = "Interview request removed.";
            return RedirectToAction("ActiveRequests");
        }

        // ── WORKER DECISIONS (RN WorkerDecisionScreen: search + skills + slot + finalize) ──
        public async Task<IActionResult> WorkerDecisions(string? search = null)
        {
            int clientId = ClientId;
            var rows = await _context.Hiring.AsNoTracking()
                .Include(h => h.Interview).ThenInclude(i => i!.Worker)
                .Include(h => h.Interview).ThenInclude(i => i!.Slot)
                .Where(h => h.Interview != null && h.Interview.ClientId == clientId)
                .OrderByDescending(h => h.HiringDate)
                .ThenByDescending(h => h.HiringId)
                .Select(h => new
                {
                    InterviewId = h.InterviewId ?? 0,
                    h.HiringId,
                    WorkerId = h.Interview!.WorkerId ?? 0,
                    WorkerName = h.Interview.Worker != null ? h.Interview.Worker.Name ?? "Worker" : "Worker",
                    WorkerPic = h.Interview.Worker != null ? h.Interview.Worker.Picture : null,
                    CategoryName = _context.WorkerCategories
                        .Where(wc => wc.WorkerId == h.Interview.WorkerId)
                        .OrderByDescending(wc => wc.IsPrimary)
                        .Join(_context.Categories, wc => wc.CategoryId, c => c.CategoryId, (wc, c) => c.CategoryName)
                        .FirstOrDefault() ?? "General Worker",
                    Location = h.Address ?? h.Interview.Address ?? "N/A",
                    JobType = h.Interview.JobType ?? "Full-Time",
                    IsExclusive = h.Interview.IsExclusive && h.Interview.JobType == "Full-Time",
                    Status = h.Interview.Status ?? "",
                    HiringWorkerDecision = h.WorkerDecision ?? "Pending",
                    HiringDecision = h.HiringDecision ?? "Pending",
                    SlotStart = h.Interview.Slot != null ? (TimeSpan?)h.Interview.Slot.StartTime : null,
                    SlotEnd = h.Interview.Slot != null ? (TimeSpan?)h.Interview.Slot.EndTime : null,
                    h.HiringDate
                }).ToListAsync();

            var skillsMap = await ReviewHelpers.LoadSelectedSkillsLookupAsync(
                _context, rows.Where(r => r.InterviewId > 0).Select(r => r.InterviewId));

            var list = rows.Select(r =>
            {
                skillsMap.TryGetValue(r.InterviewId, out var skills);
                return new InterviewRequestVM
                {
                    InterviewId = r.InterviewId,
                    HiringId = r.HiringId,
                    OtherPartyId = r.WorkerId,
                    OtherPartyName = r.WorkerName,
                    OtherPartyPicture = r.WorkerPic,
                    CategoryName = r.CategoryName,
                    Location = r.Location,
                    JobType = r.JobType,
                    IsExclusive = r.IsExclusive,
                    Status = r.Status,
                    HiringWorkerDecision = r.HiringWorkerDecision,
                    HiringDecision = r.HiringDecision,
                    SlotStart = r.SlotStart.HasValue ? DateTime.Today.Add(r.SlotStart.Value).ToString("hh:mm tt") : null,
                    SlotEnd = r.SlotEnd.HasValue ? DateTime.Today.Add(r.SlotEnd.Value).ToString("hh:mm tt") : null,
                    HiringDate = r.HiringDate?.ToString("dd MMM yyyy"),
                    SelectedSkills = skills ?? new List<string>()
                };
            }).ToList();

            if (!string.IsNullOrWhiteSpace(search))
            {
                var q = search.Trim();
                list = list.Where(item =>
                    item.OtherPartyName.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                    item.CategoryName.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                    item.SelectedSkills.Any(s => s.Contains(q, StringComparison.OrdinalIgnoreCase))).ToList();
            }

            ViewBag.Search = search;
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

        // ── RESIGNATIONS (RN ResignationsScreen + ResignationScreen) ──────────────
        public async Task<IActionResult> Resignations()
        {
            int clientId = ClientId;
            await _workflow.ProcessDueAsync(clientId: clientId);
            var list = await _context.Resignations.AsNoTracking()
                .Include(r => r.Interview).ThenInclude(i => i!.Worker)
                .Where(r => r.Interview != null && r.Interview.ClientId == clientId)
                .OrderByDescending(r => r.SubmittedDate)
                .ThenByDescending(r => r.ResignationId)
                .Select(r => new ResignationVM
                {
                    ResignationId = r.ResignationId,
                    InterviewId = r.InterviewId ?? 0,
                    WorkerId = r.Interview!.WorkerId ?? 0,
                    WorkerName = r.Interview.Worker != null ? r.Interview.Worker.Name ?? "Worker" : "Worker",
                    WorkerAvatar = r.Interview.Worker != null ? r.Interview.Worker.Picture : null,
                    Reason = r.ResignationReason,
                    LastWorkingDate = r.LastWorkingDate.ToString("dd MMM yyyy"),
                    SubmittedDate = r.SubmittedDate != null ? r.SubmittedDate.Value.ToString("dd MMM yyyy") : "",
                    Status = r.Interview.Status ?? "",
                    IsConfirmed = r.AcceptedAtUtc != null || r.Interview.Status == "Resigned",
                    AcceptanceMode = r.AcceptanceMode
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
            int workerId = interview?.WorkerId ?? 0;

            var workerRole = await _context.WorkerCategories.AsNoTracking()
                .Where(wc => wc.WorkerId == workerId)
                .OrderByDescending(wc => wc.IsPrimary)
                .Join(_context.Categories, wc => wc.CategoryId, c => c.CategoryId, (wc, c) => c.CategoryName)
                .FirstOrDefaultAsync() ?? "Hired Worker";

            var reviewRows = await _context.Reviews.AsNoTracking()
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
                        .ToList(),
                    SkillRatings = x.ReviewSkillRatings
                        .OrderBy(s => s.Skill.SkillName).ThenBy(s => s.SkillsId)
                        .Select(s => new CriteriaScoreVM { Name = s.Skill.SkillName ?? "Skill", Score = s.Score })
                        .ToList()
                }).ToListAsync();

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
                    EndReasonType = w.EndReasonType,
                    EndReason = w.EndReason,
                    Criteria = row.Criteria,
                    SkillRatings = row.SkillRatings
                };
            }

            var workerReview = MapReview("Worker", interview?.Worker?.Name ?? "Worker");
            var clientReview = MapReview("Client", "You");

            int totalDays = 7;
            int remainingDays = 0;
            double progress = 1.0;
            if (!isConfirmed)
            {
                if (r.NoticeEndsAtUtc.HasValue)
                {
                    var remHours = (r.NoticeEndsAtUtc.Value - DateTime.UtcNow).TotalDays;
                    remainingDays = Math.Max(0, (int)Math.Ceiling(remHours));
                    progress = Math.Clamp((totalDays - remHours) / totalDays, 0.0, 1.0);
                }
                else if (r.LastWorkingDate != default)
                {
                    var rem = (r.LastWorkingDate.ToDateTime(TimeOnly.MinValue).Date - DateTime.Today).Days;
                    remainingDays = Math.Max(0, rem);
                    progress = Math.Clamp((double)(totalDays - remainingDays) / totalDays, 0.0, 1.0);
                }
            }

            var vm = new ResignationVM
            {
                ResignationId = r.ResignationId,
                InterviewId = interviewId,
                WorkerId = workerId,
                WorkerName = interview?.Worker?.Name ?? "Worker",
                WorkerRole = workerRole,
                WorkerAvatar = interview?.Worker?.Picture,
                Reason = r.ResignationReason,
                LastWorkingDate = r.LastWorkingDate.ToString("dd MMM yyyy"),
                SubmittedDate = r.SubmittedDate != null ? r.SubmittedDate.Value.ToString("dd MMM yyyy") : "",
                Status = interview?.Status ?? "",
                TotalNoticeDays = totalDays,
                RemainingDays = remainingDays,
                Progress = progress,
                NoticeEndsAt = r.NoticeEndsAtUtc?.ToString("dd MMM yyyy HH:mm 'UTC'"),
                IsConfirmed = isConfirmed,
                AcceptanceMode = r.AcceptanceMode,
                AcceptedAt = r.AcceptedAtUtc?.ToString("dd MMM yyyy HH:mm 'UTC'"),
                WorkerReview = workerReview,
                ClientReview = clientReview,
                CriteriaGroups = clientReview == null
                    ? await ReviewHelpers.LoadCommonCriteriaGroupsAsync(_context)
                    : new List<CriteriaGroupVM>(),
                SelectedSkills = clientReview == null
                    ? await ReviewHelpers.LoadSelectedSkillsForInterviewAsync(_context, interviewId)
                    : new List<SelectedSkillInputVM>()
            };
            return View(vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ConfirmResignation(int interviewId, int? resignationId, string? comment,
            List<CriteriaScoreInput>? criteriaScores, List<SkillScoreInput>? skillScores)
        {
            try
            {
                var notice = await _workflow.ConfirmWithClientReviewAsync(ClientId, interviewId, resignationId, async () =>
                {
                    if (string.IsNullOrWhiteSpace(comment) || comment.Trim().Length > 4000)
                        throw new ResignationWorkflowException(400, "Review remarks are required and must be at most 4,000 characters.");

                    var criteria = await ReviewHelpers.BuildCommonCriteriaRatingsAsync(_context, interviewId, criteriaScores);
                    if (criteria.Error != null)
                        throw new ResignationWorkflowException(400, criteria.Error);

                    var skills = await ReviewHelpers.BuildSkillRatingsAsync(_context, interviewId, skillScores);
                    if (skills.Error != null)
                        throw new ResignationWorkflowException(400, skills.Error);

                    return new Review
                    {
                        Rating = criteria.Average,
                        Comment = comment.Trim(),
                        ReviewCriteriaRatings = criteria.Rows,
                        ReviewSkillRatings = skills.Rows
                    };
                });
                TempData["Success"] = notice.AcceptanceMode == "Automatic"
                    ? "Your review of the worker was saved."
                    : "Resignation confirmed. Your review of the worker was saved.";
                return RedirectToAction("ResignationDetail", new { id = notice.ResignationId });
            }
            catch (ResignationWorkflowException ex)
            {
                TempData["Error"] = ex.Message;
                return resignationId.HasValue ? RedirectToAction("ResignationDetail", new { id = resignationId.Value }) : RedirectToAction("Resignations");
            }
        }

        // ── TERMINATE CONTRACT (RN TerminateContractScreen — common criteria + selected skills) ──
        [HttpGet]
        public async Task<IActionResult> Terminate(int interviewId)
        {
            var interview = await _context.Interviews.Include(i => i.Worker)
                .FirstOrDefaultAsync(i => i.InterviewId == interviewId && i.ClientId == ClientId);
            if (interview == null) return NotFound();
            var canTerminate = !ContractRules.IsEnded(interview.Status) && await _context.Hiring.AnyAsync(h =>
                h.InterviewId == interviewId && h.HiringDecision == "Accepted");
            if (!canTerminate) { TempData["Error"] = "Only an active hired contract can be terminated."; return RedirectToAction("Dashboard"); }

            int workerId = interview.WorkerId ?? 0;
            var workerRole = await _context.WorkerCategories.AsNoTracking()
                .Where(wc => wc.WorkerId == workerId)
                .OrderByDescending(wc => wc.IsPrimary)
                .Join(_context.Categories, wc => wc.CategoryId, c => c.CategoryId, (wc, c) => c.CategoryName)
                .FirstOrDefaultAsync() ?? "Hired Worker";

            ViewBag.InterviewId = interviewId;
            ViewBag.WorkerId = workerId;
            ViewBag.WorkerName = interview.Worker?.Name ?? "Worker";
            ViewBag.WorkerRole = workerRole;
            ViewBag.WorkerPicture = interview.Worker?.Picture;
            ViewBag.CriteriaGroups = await ReviewHelpers.LoadCommonCriteriaGroupsAsync(_context);
            ViewBag.SelectedSkills = await ReviewHelpers.LoadSelectedSkillsForInterviewAsync(_context, interviewId);
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Terminate(int interviewId, string reason, string? remarks,
            List<CriteriaScoreInput>? criteriaScores, List<SkillScoreInput>? skillScores)
        {
            if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 4000 || remarks?.Length > 4000)
            {
                TempData["Error"] = "A termination reason of at most 4,000 characters is required; remarks have the same limit.";
                return RedirectToAction("Terminate", new { interviewId });
            }

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

            var criteria = await ReviewHelpers.BuildCommonCriteriaRatingsAsync(_context, interviewId, criteriaScores);
            if (criteria.Error != null)
            {
                await transaction.RollbackAsync();
                TempData["Error"] = criteria.Error;
                return RedirectToAction("Terminate", new { interviewId });
            }

            var skills = await ReviewHelpers.BuildSkillRatingsAsync(_context, interviewId, skillScores);
            if (skills.Error != null)
            {
                await transaction.RollbackAsync();
                TempData["Error"] = skills.Error;
                return RedirectToAction("Terminate", new { interviewId });
            }

            _context.Reviews.Add(new Review
            {
                InterviewId = interviewId,
                Rating = criteria.Average,
                Comment = remarks?.Trim(),
                ReviewerRole = "Client",
                ReviewDate = ResignationWorkflowService.BusinessLocal(DateTime.UtcNow),
                ReviewCriteriaRatings = criteria.Rows,
                ReviewSkillRatings = skills.Rows
            });

            interview.Status = "Terminated";
            _context.Terminations.Add(new Termination
            {
                InterviewId = interviewId,
                TerminatedDate = DateOnly.FromDateTime(DateTime.Now),
                TerminatedReason = reason.Trim()
            });
            await _workflow.SetPartTimeSlotActiveAsync(interview, true);
            await _rules.ReleaseExclusiveAvailabilityAsync(interview);
            await _context.SaveChangesAsync();
            await transaction.CommitAsync();
            TempData["Success"] = "Contract terminated successfully.";
            return RedirectToAction("Dashboard");
        }

        // ── PROFILE ───────────────────────────────────────────────────────────────
        public async Task<IActionResult> Profile()
        {
            var client = await _context.Clients.FindAsync(ClientId);
            if (client == null) return NotFound();

            var workerReviewRows = await _context.Reviews.AsNoTracking()
                .Where(r => r.Interview != null && r.Interview.ClientId == ClientId && r.ReviewerRole == "Worker")
                .OrderByDescending(r => r.ReviewDate)
                .ThenByDescending(r => r.ReviewId)
                .Select(r => new
                {
                    r.InterviewId,
                    Rating = r.Rating ?? 0m,
                    r.Comment,
                    r.ReviewDate,
                    EndType = r.Interview!.Status,
                    ReviewerName = r.Interview.Worker != null ? r.Interview.Worker.Name : "Anonymous",
                    ReviewerId = r.Interview.WorkerId,
                    Criteria = r.ReviewCriteriaRatings
                        .OrderBy(c => c.Criteria.SortOrder).ThenBy(c => c.CriteriaId)
                        .Select(c => new CriteriaScoreVM { Name = c.Criteria.CriteriaName, Score = c.Score })
                        .ToList()
                }).ToListAsync();

            var windows = await ReviewHelpers.LoadContractWindowsAsync(_context,
                workerReviewRows.Where(x => x.InterviewId.HasValue).Select(x => x.InterviewId!.Value).Distinct().ToList(),
                workerReviewRows.Where(x => x.InterviewId.HasValue)
                    .GroupBy(x => x.InterviewId!.Value)
                    .ToDictionary(g => g.Key, g => g.Max(x => x.ReviewDate)));

            ViewBag.Reviews = workerReviewRows.Select(r =>
            {
                var hasWin = windows.TryGetValue(r.InterviewId ?? 0, out var w);
                return new ReviewVM
                {
                    Rating = r.Rating,
                    Comment = r.Comment,
                    ReviewerRole = "Worker",
                    ReviewerName = r.ReviewerName ?? "Anonymous",
                    ReviewDate = r.ReviewDate != null ? r.ReviewDate.Value.ToString("dd MMM yyyy") : "",
                    WorkedPeriod = hasWin ? ReviewHelpers.FormatWorkedPeriod(w.From, w.To) : null,
                    EndType = r.EndType,
                    EndReasonType = hasWin ? w.EndReasonType : null,
                    EndReason = hasWin ? w.EndReason : null,
                    Criteria = r.Criteria,
                    ReviewerId = r.ReviewerId
                };
            }).ToList();

            return View(client);
        }

        // ── EDIT PROFILE ───────────────────────────────────────────────────────────
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
