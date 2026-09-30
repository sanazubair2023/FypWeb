using Fyp_Backend.Models;
using FypWeb.Helpers;
using FypWeb.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FypWeb.Controllers
{
    [Authorize(Roles = "Client")]
    public class ClientController : Controller
    {
        private readonly Fyp1Context _context;
        public ClientController(Fyp1Context context) => _context = context;

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

            foreach (var w in workerList)
            {
                var ratings = await _context.Reviews
                    .Where(r => r.Interview != null && r.Interview.WorkerId == w.WorkerId)
                    .Select(r => r.Rating).ToListAsync();
                double avg = ratings.Any() ? Math.Round(ratings.Average(r => (double)r!), 1) : 0.0;

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
                    IsPoliceVerified = verified
                });
            }

            ViewBag.Categories = await _context.Categories.Select(c => c.CategoryName).ToListAsync();
            ViewBag.Habits = await _context.Habits.Where(h => h.IsActive).OrderBy(h => h.SortOrder)
                .Select(h => new { h.HabitId, h.Name }).ToListAsync();
            ViewBag.Search = search;
            return View(results);
        }

        // ── WORKER DETAIL ─────────────────────────────────────────────────────────
        public async Task<IActionResult> WorkerDetail(int id)
        {
            var worker = await _context.Workers
                .Include(w => w.Experiences)
                .FirstOrDefaultAsync(w => w.WorkerId == id);
            if (worker == null) return NotFound();

            var client = await _context.Clients.FindAsync(ClientId);

            var reviews = await _context.Reviews
                .Where(r => r.Interview != null && r.Interview.WorkerId == id)
                .OrderByDescending(r => r.ReviewDate)
                .Select(r => new ReviewVM
                {
                    Rating = r.Rating ?? 0,
                    Comment = r.Comment,
                    ReviewerRole = r.ReviewerRole ?? "Client",
                    ReviewDate = r.ReviewDate != null ? r.ReviewDate.Value.ToString("dd MMM yyyy") : ""
                }).ToListAsync();

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

            bool hasActive = await _context.Interviews.AnyAsync(i =>
                i.ClientId == ClientId && i.WorkerId == id &&
                i.Status != "Rejected" && i.Status != "Completed" &&
                i.Status != "Terminated" && i.Status != "Resigned");

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
                CompanyCertificates = companyCerts
            };
            return View(vm);
        }

        // ── BOOK INTERVIEW ────────────────────────────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> BookInterview(int workerId, string? date = null)
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
            return View(vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> BookInterview(int workerId, DateTime interviewDate, string? address,
            int? slotId, bool isResidenceProvided = false)
        {
            var client = await _context.Clients.FindAsync(ClientId);
            if (client == null) { TempData["Error"] = "Client profile not found."; return RedirectToAction("FindService"); }
            var worker = await _context.Workers.FindAsync(workerId);
            if (worker == null) { TempData["Error"] = "Worker not found."; return RedirectToAction("FindService"); }

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

            if (model.JobType == "Part-Time")
            {
                model.IsResidenceProvided = false;
                if (slotId == null)
                {
                    TempData["Error"] = "Please select one of the worker's time slots.";
                    return RedirectToAction("BookInterview", new { workerId });
                }
                var chosen = await _context.WorkerTimeSlots.FindAsync(slotId.Value);
                if (chosen == null || chosen.WorkerId != workerId)
                {
                    TempData["Error"] = "That time slot does not belong to this worker.";
                    return RedirectToAction("BookInterview", new { workerId });
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
                    return RedirectToAction("BookInterview", new { workerId });
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

            var vm = new ResignationVM
            {
                ResignationId = r.ResignationId,
                InterviewId = r.InterviewId ?? 0,
                WorkerName = r.Interview?.Worker?.Name ?? "Worker",
                WorkerAvatar = r.Interview?.Worker?.Picture,
                Reason = r.ResignationReason,
                LastWorkingDate = r.LastWorkingDate.ToString("dd MMM yyyy"),
                SubmittedDate = r.SubmittedDate != null ? r.SubmittedDate.Value.ToString("dd MMM yyyy") : "",
                Status = r.Interview?.Status ?? ""
            };
            return View(vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ConfirmResignation(int interviewId, int rating, string? comment)
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
                _context.Reviews.Add(new Review
                {
                    InterviewId = interviewId,
                    Rating = rating,
                    Comment = comment,
                    ReviewerRole = "Client",
                    ReviewDate = DateTime.Now
                });
            }
            interview.Status = "Resigned";
            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            TempData["Success"] = "Resignation confirmed and review submitted.";
            return RedirectToAction("Resignations");
        }

        // ── TERMINATE CONTRACT ────────────────────────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> Terminate(int interviewId)
        {
            var interview = await _context.Interviews
                .Include(i => i.Worker).FirstOrDefaultAsync(i => i.InterviewId == interviewId);
            if (interview == null) return NotFound();

            ViewBag.InterviewId = interviewId;
            ViewBag.WorkerName = interview.Worker?.Name ?? "Worker";
            ViewBag.WorkerPicture = interview.Worker?.Picture;
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Terminate(int interviewId, string reason, int rating, string? remarks)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();
            var interview = await _context.Interviews.FindAsync(interviewId);
            if (interview == null) { TempData["Error"] = "Job record not found."; return RedirectToAction("Dashboard"); }

            if (interview.Status == "Terminated") { TempData["Error"] = "Already terminated."; return RedirectToAction("Dashboard"); }

            bool alreadyReviewed = await _context.Reviews.AnyAsync(r => r.InterviewId == interviewId && r.ReviewerRole == "Client");
            if (!alreadyReviewed)
            {
                _context.Reviews.Add(new Review
                {
                    InterviewId = interviewId,
                    Rating = rating,
                    Comment = remarks,
                    ReviewerRole = "Client",
                    ReviewDate = DateTime.Now
                });
            }
            interview.Status = "Terminated";
            _context.Terminations.Add(new Termination
            {
                InterviewId = interviewId,
                TerminatedDate = DateOnly.FromDateTime(DateTime.Now),
                TerminatedReason = reason
            });
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

            var reviews = await _context.Reviews
                .Where(r => r.Interview != null && r.Interview.ClientId == ClientId && r.ReviewerRole == "Worker")
                .OrderByDescending(r => r.ReviewDate)
                .Select(r => new ReviewVM
                {
                    Rating = r.Rating ?? 0,
                    Comment = r.Comment,
                    ReviewerRole = r.ReviewerRole ?? "Worker",
                    ReviewDate = r.ReviewDate != null ? r.ReviewDate.Value.ToString("dd MMM yyyy") : ""
                }).ToListAsync();

            ViewBag.Reviews = reviews;
            return View(client);
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
