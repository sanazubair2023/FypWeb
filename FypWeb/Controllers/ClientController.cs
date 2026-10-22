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
    [Authorize(Roles = "Client")]
    public class ClientController : Controller
    {
        private readonly Fyp1Context _context;
        private readonly ImageService _images;
        public ClientController(Fyp1Context context, ImageService images)
        {
            _context = context;
            _images = images;
        }

        private int ClientId => User.GetId();

        // ── DASHBOARD ─────────────────────────────────────────────────────────────
        public async Task<IActionResult> Dashboard()
        {
            int clientId = ClientId;

            ViewBag.PendingCount = await _context.Interviews
                .CountAsync(i => i.ClientId == clientId && i.Status == "Pending");

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
                    slotEnd = h.Interview.Slot != null ? (TimeSpan?)h.Interview.Slot.EndTime : null
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
                    _ => "On Work"
                }
            }).ToList();

            ViewBag.HiredCount = cards.Count(c => c.Status == "On Work");
            return View(cards);
        }

        // ── FIND SERVICE (search + filters) ───────────────────────────────────────
        public async Task<IActionResult> FindService(
            string? search, List<string>? categories, string? gender, string? city,
            List<string>? subSkills, List<int>? habitIds)
        {
            IQueryable<Worker> query = _context.Workers;

            if (categories != null && categories.Any() && !categories.Contains("All"))
            {
                var normalized = categories.Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c.Trim()).ToList();
                var categoryIds = normalized.Where(c => int.TryParse(c, out _)).Select(int.Parse).ToList();
                var categoryNames = normalized.Where(c => !int.TryParse(c, out _)).ToList();
                query = query.Where(w => _context.WorkerCategories
                    .Any(wc => wc.WorkerId == w.WorkerId && _context.Categories
                        .Any(c => c.CategoryId == wc.CategoryId &&
                            (categoryIds.Contains(c.CategoryId) || categoryNames.Contains(c.CategoryName)))));
            }

            if (!string.IsNullOrEmpty(gender) && gender != "Both")
                query = query.Where(w => w.Gender == gender);

            if (!string.IsNullOrEmpty(city))
                query = query.Where(w => w.Address != null && w.Address.Contains(city));

            if (!string.IsNullOrEmpty(search))
                query = query.Where(w => w.Name != null && w.Name.Contains(search));

            if (subSkills != null && subSkills.Any())
                foreach (var skillName in subSkills)
                    query = query.Where(w => _context.WorkerCategories
                        .Any(wc => wc.WorkerId == w.WorkerId && _context.Skills.Any(s => s.SkillsId == wc.SkillsId && s.SkillName == skillName)));

            if (habitIds != null && habitIds.Any())
                foreach (var hid in habitIds.Where(id => id > 0).Distinct())
                {
                    int wanted = hid;
                    query = query.Where(w => _context.WorkerHabits.Any(wh => wh.WorkerId == w.WorkerId && wh.HabitId == wanted));
                }

            var workerList = await query.ToListAsync();
            var results = new List<WorkerCardVM>();

            // "Certified" chip (backend GetWorkers): legacy company certifications
            // UNION non-revoked course certificates — one set-based query.
            var certifiedWorkerIds = (await _context.WorkerCertifications
                    .Select(wc => wc.WorkerID)
                    .Union(_context.CourseCertificates
                        .Where(cc => !cc.IsRevoked)
                        .Select(cc => cc.WorkerID))
                    .ToListAsync())
                .ToHashSet();

            foreach (var w in workerList)
            {
                var ratings = await _context.Reviews
                    .Where(r => r.Interview != null && r.Interview.WorkerId == w.WorkerId)
                    .Select(r => r.Rating).ToListAsync();
                double avg = ratings.Any() ? Math.Round(ratings.Average(r => (double)(r ?? 0m)), 1) : 0.0;

                var catNames = await _context.WorkerCategories
                    .Where(wc => wc.WorkerId == w.WorkerId)
                    .Join(_context.Categories, wc => wc.CategoryId, c => c.CategoryId, (wc, c) => c.CategoryName)
                    .Where(n => !string.IsNullOrEmpty(n)).Distinct().ToListAsync();

                bool verified = await _context.PoliceRecords
                    .AnyAsync(pr => pr.WorkerID == w.WorkerId && !pr.IsRevoked && pr.ExpiryDate > DateTime.Now);

                results.Add(new WorkerCardVM
                {
                    Id = w.WorkerId,
                    Name = w.Name ?? "",
                    Role = catNames.FirstOrDefault() ?? "General",
                    City = w.Address ?? "N/A",
                    Salary = w.Salary != null ? "Rs." + w.Salary.ToString() : "Not Set",
                    Phone = w.Phone,
                    Picture = w.Picture,
                    Rating = avg.ToString("F1"),
                    Gender = w.Gender ?? "N/A",
                    Categories = catNames,
                    AvailableStatus = w.AvailableStatus ?? false,
                    IsPoliceVerified = verified,
                    IsCompanyCertified = certifiedWorkerIds.Contains(w.WorkerId)
                });
            }

            ViewBag.Categories = await _context.Categories.Select(c => c.CategoryName).ToListAsync();
            ViewBag.Habits = await _context.Habits.Where(h => h.IsActive).OrderBy(h => h.SortOrder)
                .Select(h => new { h.HabitId, h.Name }).ToListAsync();
            ViewBag.Search = search;
            return View(results);
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
            ViewBag.Habits = await _context.Habits.Where(h => h.IsActive)
                .OrderBy(h => h.SortOrder).ThenBy(h => h.Name)
                .Select(h => new { h.HabitId, h.Name }).ToListAsync();
            ViewBag.Cities = new[]
            {
                "Islamabad", "Rawalpindi", "Lahore", "Karachi",
                "Faisalabad", "Peshawar", "Multan", "Quetta",
                "Gujranwala", "Sialkot"
            };
            ViewBag.Gender = gender ?? "";
            ViewBag.City = city ?? "";
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
            var slots = await _context.WorkerTimeSlots
                .Where(s => s.WorkerId == id)
                .OrderBy(s => s.StartTime)
                .Select(s => new SlotVM
                {
                    Id = s.Id,
                    StartTime = s.StartTime.ToString(@"hh\:mm"),
                    EndTime = s.EndTime.ToString(@"hh\:mm")
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

            // Pass this flag to ViewBag so the UI/view can handle it if needed
            ViewBag.CanHire = !isTerminatedByThisClient;
            ViewBag.ContractStatus = isTerminatedByThisClient ? "Terminated by You" : "";

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
                Address = worker.Address,
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
        public async Task<IActionResult> BookInterview(int workerId, string? date = null, string? time = null)
        {
            var worker = await _context.Workers.FindAsync(workerId);
            if (worker == null) return NotFound();
            var client = await _context.Clients.FindAsync(ClientId);

            bool isPartTime = GeoHelper.ClassifyJobType(client, worker) == "Part-Time";
            double workerRadius = worker.Radius > 0 ? worker.Radius : 5.0;
            double distanceKm = 0;
            if (client != null && client.Latitude.HasValue && client.Longitude.HasValue && worker.Latitude.HasValue && worker.Longitude.HasValue)
                distanceKm = GeoHelper.CalculateHaversineDistance(
                    Convert.ToDouble(client.Latitude.Value), Convert.ToDouble(client.Longitude.Value),
                    Convert.ToDouble(worker.Latitude.Value), Convert.ToDouble(worker.Longitude.Value));

            DateTime day = DateTime.Today;
            if (!string.IsNullOrWhiteSpace(date) && DateTime.TryParse(date, out var parsed)) day = parsed.Date;
            var dayStart = day; var dayEnd = day.AddDays(1);

            var rawSlots = await _context.WorkerTimeSlots.Where(s => s.WorkerId == workerId)
                .OrderBy(s => s.StartTime).ToListAsync();

            var bookedSlotIds = await _context.Interviews
                .Where(i => i.WorkerId == workerId && i.SlotId != null && i.InterviewDate != null &&
                            i.InterviewDate >= dayStart && i.InterviewDate < dayEnd &&
                            i.Status != "Rejected" && i.Status != "JobRejected" &&
                            i.Status != "Terminated" && i.Status != "Resigned" && i.Status != "Completed")
                .Select(i => i.SlotId!.Value).ToListAsync();

            var vm = new BookInterviewVM
            {
                WorkerId = workerId,
                WorkerName = worker.Name ?? "Worker",
                WorkerPicture = worker.Picture,
                IsPartTime = isPartTime,
                HasSlots = rawSlots.Any(),
                Radius = workerRadius,
                DistanceKm = Math.Round(distanceKm, 2),
                ClientAddress = client?.Address ?? "",
                Slots = rawSlots.Select(s => new SlotVM
                {
                    Id = s.Id,
                    StartTime = s.StartTime.ToString(@"hh\:mm"),
                    EndTime = s.EndTime.ToString(@"hh\:mm"),
                    IsTaken = bookedSlotIds.Contains(s.Id)
                }).ToList()
            };
            ViewBag.Date = day.ToString("yyyy-MM-dd");
            // Clock picker (RN "Interview Time" selector): keeps the chosen time
            // across the date-change reload; defaults to the current time.
            ViewBag.Time = TimeSpan.TryParse(time, out var pickedTime)
                ? pickedTime.ToString(@"hh\:mm")
                : DateTime.Now.ToString("HH:mm");
            return View(vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> BookInterview(int workerId, DateTime interviewDate, string? address,
            int? slotId, bool isResidenceProvided = false, string? interviewTime = null)
        {
            var client = await _context.Clients.FindAsync(ClientId);
            if (client == null) { TempData["Error"] = "Client profile not found."; return RedirectToAction("FindService"); }
            var worker = await _context.Workers.FindAsync(workerId);
            if (worker == null) { TempData["Error"] = "Worker not found."; return RedirectToAction("FindService"); }

            // Prevent the client who terminated this worker from booking them again
            bool isTerminatedByThisClient = await _context.Interviews.AnyAsync(i =>
                i.ClientId == ClientId && i.WorkerId == workerId && i.Status == "Terminated");
            if (isTerminatedByThisClient)
            {
                TempData["Error"] = "You cannot hire a worker whose contract you have previously terminated.";
                return RedirectToAction("WorkerDetail", new { id = workerId });
            }

            // RN posts the full datetime (date picker + clock picker combined);
            // here the two inputs are merged into one InterviewDate value.
            if (TimeSpan.TryParse(interviewTime, out var picked))
            {
                interviewDate = interviewDate.Date.Add(picked);
            }

            bool alreadyPending = await _context.Interviews.AnyAsync(i =>
                i.ClientId == ClientId && i.WorkerId == workerId &&
                (i.Status == "Pending" || i.Status == "Approved"));
            if (alreadyPending)
            {
                TempData["Error"] = "You already have an active request with this worker.";
                return RedirectToAction("WorkerDetail", new { id = workerId });
            }

            var model = new Interview
            {
                ClientId = ClientId,
                WorkerId = workerId,
                InterviewDate = interviewDate,
                Address = string.IsNullOrWhiteSpace(address) ? client.Address : address,
                Status = "Pending",
                WorkerDecision = "Pending",
                JobType = GeoHelper.ClassifyJobType(client, worker)
            };

            // Route values kept on validation errors so the user's date/time
            // picks survive the round-trip.
            var retryRoute = new { workerId, date = interviewDate.ToString("yyyy-MM-dd"), time = interviewTime };

            if (model.JobType == "Part-Time")
            {
                model.IsResidenceProvided = false;
                if (slotId == null)
                {
                    TempData["Error"] = "Please select one of the worker's time slots.";
                    return RedirectToAction("BookInterview", retryRoute);
                }
                var chosen = await _context.WorkerTimeSlots.FindAsync(slotId.Value);
                if (chosen == null || chosen.WorkerId != workerId)
                {
                    TempData["Error"] = "That time slot does not belong to this worker.";
                    return RedirectToAction("BookInterview", retryRoute);
                }
                var dayStart = interviewDate.Date; var dayEnd = dayStart.AddDays(1);
                bool slotTaken = await _context.Interviews.AnyAsync(i =>
                    i.WorkerId == workerId && i.SlotId == slotId && i.InterviewDate != null &&
                    i.InterviewDate >= dayStart && i.InterviewDate < dayEnd &&
                    i.Status != "Rejected" && i.Status != "JobRejected" &&
                    i.Status != "Terminated" && i.Status != "Resigned" && i.Status != "Completed");
                if (slotTaken)
                {
                    TempData["Error"] = "That time slot is already booked for this date. Please pick another.";
                    return RedirectToAction("BookInterview", retryRoute);
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
                    JobType = i.JobType ?? "Full-Time"
                }).ToListAsync();
            return View(accepted);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateHiring(int interviewId, string? address)
        {
            var interview = await _context.Interviews.FindAsync(interviewId);
            if (interview == null) { TempData["Error"] = "Interview not found."; return RedirectToAction("ActiveRequests"); }

            interview.Status = "Approved";
            string? finalAddress = string.IsNullOrWhiteSpace(address) ? interview.Address : address;

            _context.Hiring.Add(new Hiring
            {
                InterviewId = interviewId,
                WorkerDecision = "Pending",
                HiringDecision = "Pending",
                Address = finalAddress,
                HiringDate = DateTime.Now
            });
            await _context.SaveChangesAsync();

            TempData["Success"] = "Interview approved. Job offer sent to the worker.";
            return RedirectToAction("ActiveRequests");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> WithdrawRequest(int interviewId)
        {
            var interview = await _context.Interviews.FindAsync(interviewId);
            if (interview != null)
            {
                _context.Interviews.Remove(interview);
                await _context.SaveChangesAsync();
            }
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
            var hiring = await _context.Hiring.FindAsync(hiringId);
            if (hiring == null) { TempData["Error"] = "Hiring record not found."; return RedirectToAction("WorkerDecisions"); }

            hiring.HiringDecision = "Accepted";
            hiring.HiringDate = DateTime.Now;
            var interview = await _context.Interviews.FindAsync(hiring.InterviewId);
            if (interview != null) interview.Status = "Finalized";
            await _context.SaveChangesAsync();

            TempData["Success"] = "Worker hired successfully!";
            return RedirectToAction("WorkerDecisions");
        }

        // ── RESIGNATIONS ──────────────────────────────────────────────────────────
        public async Task<IActionResult> Resignations()
        {
            int clientId = ClientId;
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
            var r = await _context.Resignations
                .Include(x => x.Interview).ThenInclude(i => i!.Worker)
                .FirstOrDefaultAsync(x => x.ResignationId == id);
            if (r == null) return NotFound();

            var interview = r.Interview;
            bool isConfirmed = interview?.Status == "Resigned";
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
                WorkerReview = MapReview("Worker", interview?.Worker?.Name ?? "Worker"),
                ClientReview = MapReview("Client", "You"),
                CriteriaGroups = isConfirmed ? new List<CriteriaGroupVM>() : await LoadCriteriaGroupsAsync(interview?.WorkerId ?? 0)
            };
            return View(vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ConfirmResignation(int interviewId, string? comment,
            List<CriteriaScoreInput>? criteriaScores, int? rating)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();
            var interview = await _context.Interviews.FindAsync(interviewId);
            if (interview == null) { TempData["Error"] = "Interview not found."; return RedirectToAction("Resignations"); }

            var resignation = await _context.Resignations
                .Where(r => r.InterviewId == interviewId).OrderByDescending(r => r.SubmittedDate).FirstOrDefaultAsync();
            if (resignation == null) { TempData["Error"] = "No resignation on record."; return RedirectToAction("Resignations"); }

            if (interview.Status == "Resigned") { TempData["Error"] = "Already confirmed."; return RedirectToAction("Resignations"); }

            bool alreadyReviewed = await _context.Reviews.AnyAsync(r => r.InterviewId == interviewId && r.ReviewerRole == "Client");
            if (!alreadyReviewed)
            {
                // The client's closing review is criteria-scored; the overall rating
                // is derived from the criteria, never sent by the form.
                var criteria = await BuildCriteriaRatingsAsync(interviewId, criteriaScores);
                if (criteria.Error != null)
                {
                    TempData["Error"] = criteria.Error;
                    return RedirectToAction("ResignationDetail", new { id = resignation.ResignationId });
                }

                _context.Reviews.Add(new Review
                {
                    InterviewId = interviewId,
                    Rating = criteria.Average,
                    Comment = comment,
                    ReviewerRole = "Client",
                    ReviewDate = DateTime.Now,
                    ReviewCriteriaRatings = criteria.Rows
                });
            }
            interview.Status = "Resigned";

            // Resignation ke baad worker dobara available ho jaye (global pool ke liye)
            var worker = await _context.Workers.FindAsync(interview.WorkerId);
            if (worker != null)
            {
                worker.AvailableStatus = true;
            }

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            TempData["Success"] = "Resignation confirmed and review submitted.";
            return RedirectToAction("Resignations");
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
            var interview = await _context.Interviews
                .Include(i => i.Worker).FirstOrDefaultAsync(i => i.InterviewId == interviewId);
            if (interview == null) return NotFound();

            ViewBag.InterviewId = interviewId;
            ViewBag.WorkerName = interview.Worker?.Name ?? "Worker";
            ViewBag.WorkerPicture = interview.Worker?.Picture;
            ViewBag.CriteriaGroups = await LoadCriteriaGroupsAsync(interview.WorkerId ?? 0);
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Terminate(int interviewId, string reason, string? remarks,
            List<CriteriaScoreInput>? criteriaScores, int? rating)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();
            var interview = await _context.Interviews.FindAsync(interviewId);
            if (interview == null) { TempData["Error"] = "Job record not found."; return RedirectToAction("Dashboard"); }

            if (interview.Status == "Terminated") { TempData["Error"] = "Already terminated."; return RedirectToAction("Dashboard"); }

            // Validate and score the per-criterion ratings before writing anything.
            // The legacy single-star "rating" parameter is ignored (kept for binding
            // compatibility with the old form).
            var criteria = await BuildCriteriaRatingsAsync(interviewId, criteriaScores);
            if (criteria.Error != null)
            {
                TempData["Error"] = criteria.Error;
                return RedirectToAction("Terminate", new { interviewId });
            }

            bool alreadyReviewed = await _context.Reviews.AnyAsync(r => r.InterviewId == interviewId && r.ReviewerRole == "Client");
            if (!alreadyReviewed)
            {
                // EF fills in Review_ID on the children through the navigation property,
                // so a single SaveChanges writes the review and its criteria rows together.
                _context.Reviews.Add(new Review
                {
                    InterviewId = interviewId,
                    Rating = criteria.Average,
                    Comment = remarks,
                    ReviewerRole = "Client",
                    ReviewDate = DateTime.Now,
                    ReviewCriteriaRatings = criteria.Rows
                });
            }
            interview.Status = "Terminated";
            _context.Terminations.Add(new Termination
            {
                InterviewId = interviewId,
                TerminatedDate = DateOnly.FromDateTime(DateTime.Now),
                TerminatedReason = reason
            });

            // IMPORTANT: Isay worker.AvailableStatus = false NAHI karna, taake doosre clients ke liye worker available rahay.
            // Sirf is specific client ke liye interview status "Terminated" rahegi jo ke upar check ho rahi hai.
            var worker = await _context.Workers.FindAsync(interview.WorkerId);
            if (worker != null)
            {
                worker.AvailableStatus = true; // Baqi clients ke liye worker available hi rahega
            }

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
            if (string.IsNullOrWhiteSpace(model.Name) || string.IsNullOrWhiteSpace(model.Phone)
                || string.IsNullOrWhiteSpace(model.Address) || string.IsNullOrWhiteSpace(model.Email))
            {
                ModelState.AddModelError(string.Empty, "Please fill out all fundamental profile details.");
            }

            if (!string.IsNullOrEmpty(model.Password) && model.Password != model.ConfirmPassword)
            {
                ModelState.AddModelError(string.Empty, "Passwords do not match.");
            }

            var existing = await _context.Clients.FindAsync(ClientId);
            if (existing == null) return NotFound();

            if (!ModelState.IsValid)
            {
                model.CurrentPicture = existing.Picture;
                return View(model);
            }

            try
            {
                // Optional new photo — same rule as UpdateClient: only replaces
                // the stored Picture when a valid file is supplied.
                if (model.PictureFile != null)
                {
                    string safeFileName = model.Email!.Replace("@", "_").Replace(".", "_");
                    string? imagePath = await _images.SaveImageAsync(model.PictureFile, safeFileName);
                    if (imagePath == "Invalid")
                    {
                        ModelState.AddModelError(string.Empty, "Only .jpg, .jpeg, and .png files are allowed.");
                        model.CurrentPicture = existing.Picture;
                        return View(model);
                    }
                    if (imagePath != null) existing.Picture = imagePath;
                }

                existing.Name = model.Name!;
                existing.Phone = model.Phone!;
                existing.Address = model.Address;
                existing.Email = model.Email!;

                // Blank password keeps the current one; "********" is the
                // masked sentinel the API ignores (legacy RN prefill).
                if (!string.IsNullOrEmpty(model.Password) && model.Password != "********")
                {
                    existing.Password = model.Password;
                }

                await _context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                ModelState.AddModelError(string.Empty, "Update failed: " + (ex.InnerException?.Message ?? ex.Message));
                model.CurrentPicture = existing.Picture;
                return View(model);
            }

            // Keep the cookie (topbar name/photo/email claims) in sync.
            await RefreshSignInAsync(existing.ClientId, existing.Name, existing.Picture, existing.Email);

            TempData["Success"] = "Profile updated successfully!";
            return RedirectToAction("Profile");
        }

        // Re-issues the auth cookie after a profile edit so the sidebar shows
        // the new name/photo without a re-login (same claims as Account/Login).
        private async Task RefreshSignInAsync(int clientId, string name, string? picture, string email)
        {
            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, clientId.ToString()),
                new(ClaimTypes.Name, string.IsNullOrWhiteSpace(name) ? "Client" : name),
                new(ClaimTypes.Role, "Client"),
                new("Picture", picture ?? ""),
                new("Email", email ?? "")
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

            var workers = await _context.Workers
                .Where(w => w.Latitude.HasValue && w.Longitude.HasValue)
                .Select(w => new
                {
                    w.WorkerId,
                    w.Name,
                    w.Latitude,
                    w.Longitude,
                    Role = _context.WorkerCategories
                        .Where(wc => wc.WorkerId == w.WorkerId)
                        .Join(_context.Categories, wc => wc.CategoryId, c => c.CategoryId, (wc, c) => c.CategoryName)
                        .FirstOrDefault()
                })
                .ToListAsync();

            ViewBag.Workers = workers.Select(w => new MapWorkerVM
            {
                Id = w.WorkerId,
                Name = w.Name ?? "Worker",
                Role = string.IsNullOrWhiteSpace(w.Role) ? "Service Worker" : w.Role!,
                Lat = Convert.ToDouble(w.Latitude!.Value),
                Lng = Convert.ToDouble(w.Longitude!.Value)
            }).ToList();

            return View(client);
        }

        // ── LOCATION (AJAX) ───────────────────────────────────────────────────────
        [HttpPost]
        public async Task<IActionResult> UpdateLocation([FromBody] LatLng dto)
        {
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