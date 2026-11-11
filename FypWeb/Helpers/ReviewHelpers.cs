using Fyp_Backend.Models;
using FypWeb.Services;
using FypWeb.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace FypWeb.Helpers
{
    /// <summary>
    /// Shared logic for criteria-based + selected-skill closing reviews,
    /// contract windows &amp; end reasons, per-skill rating breakdowns,
    /// and worker certificate records (1:1 port of Fyp_Backend).
    /// </summary>
    public static class ReviewHelpers
    {
        public static async Task<Dictionary<int, (DateTime? From, DateTime? To, string? EndReasonType, string? EndReason)>> LoadContractWindowsAsync(
            Fyp1Context context, List<int> interviewIds, Dictionary<int, DateTime?>? fallbackEndDates = null)
        {
            var windows = new Dictionary<int, (DateTime? From, DateTime? To, string? EndReasonType, string? EndReason)>();
            if (interviewIds == null || interviewIds.Count == 0) return windows;

            var distinctIds = interviewIds.Distinct().ToList();

            var hireDates = (await context.Hiring
                .Where(h => h.InterviewId != null && distinctIds.Contains(h.InterviewId.Value))
                .Select(h => new { InterviewId = h.InterviewId!.Value, h.HiringDate })
                .ToListAsync())
                .GroupBy(x => x.InterviewId)
                .ToDictionary(g => g.Key, g => g.Min(x => x.HiringDate));

            var interviews = (await context.Interviews
                .Where(i => distinctIds.Contains(i.InterviewId))
                .Select(i => new { i.InterviewId, i.InterviewDate, i.Status })
                .ToListAsync())
                .ToDictionary(x => x.InterviewId);

            var resignations = await context.Resignations
                .Where(r => r.InterviewId != null && distinctIds.Contains(r.InterviewId.Value))
                .Select(r => new
                {
                    InterviewId = r.InterviewId!.Value,
                    r.ResignationId,
                    r.SubmittedDate,
                    r.LastWorkingDate,
                    r.AcceptedAtUtc,
                    r.ResignationReason
                })
                .ToListAsync();

            var terminations = await context.Terminations
                .Where(t => t.InterviewId != null && distinctIds.Contains(t.InterviewId.Value))
                .Select(t => new
                {
                    InterviewId = t.InterviewId!.Value,
                    t.TerminationId,
                    t.TerminatedDate,
                    rReason = t.TerminatedReason
                })
                .ToListAsync();

            foreach (var id in distinctIds)
            {
                interviews.TryGetValue(id, out var interview);
                DateTime? from = hireDates.TryGetValue(id, out var hireDate) ? hireDate : null;
                if (from == null) from = interview?.InterviewDate;

                var termination = terminations
                    .Where(t => t.InterviewId == id)
                    .OrderByDescending(t => t.TerminatedDate)
                    .ThenByDescending(t => t.TerminationId)
                    .FirstOrDefault();

                var resignation = resignations
                    .Where(r => r.InterviewId == id)
                    .OrderByDescending(r => r.AcceptedAtUtc.HasValue)
                    .ThenByDescending(r => r.AcceptedAtUtc ?? r.SubmittedDate)
                    .ThenByDescending(r => r.ResignationId)
                    .FirstOrDefault();

                DateTime? to = null;
                if (resignation?.AcceptedAtUtc is DateTime acceptedAt &&
                    string.Equals(interview?.Status, "Resigned", StringComparison.OrdinalIgnoreCase))
                {
                    to = ResignationWorkflowService.BusinessDate(acceptedAt).ToDateTime(TimeOnly.MinValue);
                }
                else if (termination?.TerminatedDate is DateOnly terminatedDate)
                {
                    to = terminatedDate.ToDateTime(TimeOnly.MinValue);
                }
                else if (resignation != null)
                {
                    to = resignation.AcceptedAtUtc.HasValue
                        ? ResignationWorkflowService.BusinessDate(resignation.AcceptedAtUtc.Value).ToDateTime(TimeOnly.MinValue)
                        : resignation.LastWorkingDate != default
                            ? resignation.LastWorkingDate.ToDateTime(TimeOnly.MinValue)
                            : resignation.SubmittedDate;
                }

                if (to == null && fallbackEndDates != null && fallbackEndDates.TryGetValue(id, out var reviewDate))
                    to = reviewDate;

                var endReasonType = ResolveReviewEndReasonType(
                    interview?.Status, termination != null, resignation != null);

                string? endReason = endReasonType == "Terminated"
                    ? termination?.rReason
                    : endReasonType == "Resigned" ? resignation?.ResignationReason : null;
                endReason = string.IsNullOrWhiteSpace(endReason) ? null : endReason.Trim();

                windows[id] = (from, to, endReasonType, endReason);
            }

            return windows;
        }

        public static string? ResolveReviewEndReasonType(
            string? status, bool hasTermination, bool hasResignation)
        {
            var normalizedStatus = status?.Trim();
            if (string.Equals(normalizedStatus, "Terminated", StringComparison.OrdinalIgnoreCase))
                return "Terminated";
            if (string.Equals(normalizedStatus, "Resigned", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(normalizedStatus, "ResignationPending", StringComparison.OrdinalIgnoreCase))
                return "Resigned";

            if (hasTermination) return "Terminated";
            if (hasResignation) return "Resigned";
            return null;
        }

        public static string? FormatWorkedSpan(DateTime? from, DateTime? to)
        {
            if (from == null || to == null) return null;
            var f = from.Value.Date;
            var t = to.Value.Date;
            if (t < f) t = f;
            return f.Year == t.Year
                ? $"{f:dd MMM} – {t:dd MMM yyyy}"
                : $"{f:dd MMM yyyy} – {t:dd MMM yyyy}";
        }

        public static string? FormatWorkedPeriod(DateTime? from, DateTime? to)
        {
            var span = FormatWorkedSpan(from, to);
            if (span == null || from == null || to == null) return null;
            var days = (to.Value.Date - from.Value.Date).Days;
            if (days < 0) days = 0;
            return span + (days <= 1 ? " (1 day)" : $" ({days} days)");
        }

        /// <summary>
        /// Batch-loads selected sub-skill names per InterviewId without N+1 queries.
        /// </summary>
        public static async Task<Dictionary<int, List<string>>> LoadSelectedSkillsLookupAsync(
            Fyp1Context context, IEnumerable<int> interviewIds)
        {
            var ids = interviewIds.Distinct().ToList();
            if (ids.Count == 0) return new Dictionary<int, List<string>>();

            var rows = await (
                from iskill in context.InterviewSkills.AsNoTracking()
                join skill in context.Skills.AsNoTracking()
                    on iskill.SkillsId equals skill.SkillsId
                where ids.Contains(iskill.InterviewId)
                orderby skill.SkillName, skill.SkillsId
                select new
                {
                    iskill.InterviewId,
                    SkillName = skill.SkillName ?? "Skill"
                })
                .ToListAsync();

            return rows
                .GroupBy(r => r.InterviewId)
                .ToDictionary(
                    g => g.Key,
                    g => g.Select(x => x.SkillName).Distinct().ToList());
        }

        public static async Task<List<CriteriaGroupVM>> LoadCommonCriteriaGroupsAsync(Fyp1Context context)
        {
            var items = await context.ReviewCriteria.AsNoTracking()
                .Where(c => c.IsActive && c.CategoryId == null)
                .OrderBy(c => c.SortOrder)
                .ThenBy(c => c.CriteriaId)
                .Select(c => new CriteriaInputVM
                {
                    CriteriaId = c.CriteriaId,
                    Name = c.CriteriaName
                })
                .ToListAsync();

            var groups = new List<CriteriaGroupVM>();
            if (items.Count > 0)
            {
                groups.Add(new CriteriaGroupVM
                {
                    GroupName = "Common Criteria",
                    Items = items
                });
            }
            return groups;
        }

        public static async Task<List<SelectedSkillInputVM>> LoadSelectedSkillsForInterviewAsync(
            Fyp1Context context, int interviewId)
        {
            return await (
                from iskill in context.InterviewSkills.AsNoTracking()
                join skill in context.Skills.AsNoTracking()
                    on iskill.SkillsId equals skill.SkillsId
                join category in context.Categories.AsNoTracking()
                    on skill.CategoryId equals category.CategoryId into catJoin
                from cat in catJoin.DefaultIfEmpty()
                where iskill.InterviewId == interviewId
                orderby skill.SkillName, skill.SkillsId
                select new SelectedSkillInputVM
                {
                    SkillsId = skill.SkillsId,
                    Name = skill.SkillName ?? "Skill",
                    CategoryName = cat != null ? cat.CategoryName : null
                })
                .ToListAsync();
        }

        public static async Task<(decimal Average, List<ReviewCriteriaRating> Rows, string? Error)> BuildCommonCriteriaRatingsAsync(
            Fyp1Context context, int interviewId, List<CriteriaScoreInput>? submitted)
        {
            submitted ??= new List<CriteriaScoreInput>();

            if (!await context.Interviews.AnyAsync(i => i.InterviewId == interviewId))
                return (0m, new(), "Interview could not be found.");

            var requiredIds = await context.ReviewCriteria.AsNoTracking()
                .Where(c => c.IsActive && c.CategoryId == null)
                .OrderBy(c => c.SortOrder)
                .ThenBy(c => c.CriteriaId)
                .Select(c => c.CriteriaId)
                .ToListAsync();

            var requiredSet = requiredIds.ToHashSet();
            var submittedIds = submitted.Select(s => s.CriteriaId).ToHashSet();

            if (requiredSet.Count == 0)
                return (0m, new(), "No active common review criteria are configured.");

            if (submitted.Count == 0)
                return (0m, new(), "Please rate every common criterion.");

            if (submittedIds.Count != submitted.Count)
                return (0m, new(), "The same criterion was scored more than once.");

            var missing = requiredSet.Except(submittedIds).ToList();
            if (missing.Count > 0)
                return (0m, new(), $"Please rate every common criterion ({submitted.Count(s => s.Score >= 1 && s.Score <= 5)}/{requiredSet.Count} rated).");

            var unexpected = submittedIds.Except(requiredSet).ToList();
            if (unexpected.Count > 0)
                return (0m, new(), "Only common criteria may be submitted for this review.");

            if (submitted.Any(s => s.Score < 1 || s.Score > 5))
                return (0m, new(), "Each common criterion must be scored between 1 and 5.");

            var rows = submitted
                .Select(s => new ReviewCriteriaRating { CriteriaId = s.CriteriaId, Score = s.Score })
                .ToList();

            var avg = Math.Round((decimal)submitted.Average(s => s.Score), 2);
            return (avg, rows, null);
        }

        public static async Task<(List<ReviewSkillRating> Rows, string? Error)> BuildSkillRatingsAsync(
            Fyp1Context context, int interviewId, List<SkillScoreInput>? submitted)
        {
            submitted ??= new List<SkillScoreInput>();

            if (!await context.Interviews.AnyAsync(i => i.InterviewId == interviewId))
                return (new(), "Interview could not be found.");

            var requiredIds = await context.InterviewSkills.AsNoTracking()
                .Where(x => x.InterviewId == interviewId)
                .Select(x => x.SkillsId)
                .Distinct()
                .ToListAsync();

            var requiredSet = requiredIds.ToHashSet();
            var submittedIds = submitted.Select(x => x.SkillsId).ToHashSet();

            if (submittedIds.Count != submitted.Count)
                return (new(), "A selected skill was scored more than once.");

            var missing = requiredSet.Except(submittedIds).ToList();
            if (missing.Count > 0)
                return (new(), "Please rate every skill selected for this interview.");

            var unexpected = submittedIds.Except(requiredSet).ToList();
            if (unexpected.Count > 0)
                return (new(), "Only skills selected for this interview may be scored.");

            if (submitted.Any(x => x.Score < 1 || x.Score > 5))
                return (new(), "Each selected skill must be scored between 1 and 5.");

            var rows = submitted
                .Select(x => new ReviewSkillRating { SkillsId = x.SkillsId, Score = x.Score })
                .ToList();
            return (rows, null);
        }

        public sealed class WorkerSkillSummaryResult
        {
            public string PrimaryRole { get; set; } = "General Worker";
            public int? PrimaryCategoryId { get; set; }
            public List<string> Categories { get; set; } = new();
            public List<string> PrimarySkills { get; set; } = new();
            public List<SkillRatingRowVM> PrimarySkillRatings { get; set; } = new();
            public double? PrimaryCategoryAverageRating { get; set; }
            public int PrimaryCategoryRatingCount { get; set; }
            public List<PartTimeSkillGroupVM> PartTimeSkills { get; set; } = new();
        }

        public static async Task<WorkerSkillSummaryResult> LoadWorkerSkillBreakdownAsync(
            Fyp1Context context, int workerId)
        {
            var result = new WorkerSkillSummaryResult();

            var junctionData = await context.WorkerCategories.AsNoTracking()
                .Where(wc => wc.WorkerId == workerId)
                .OrderByDescending(wc => wc.IsPrimary)
                .ThenBy(wc => wc.CategoryId)
                .ThenBy(wc => wc.SkillsId)
                .ToListAsync();

            var categories = await context.Categories.AsNoTracking().ToListAsync();
            var categoryLookup = categories
                .GroupBy(c => c.CategoryId)
                .ToDictionary(g => g.Key, g => g.First().CategoryName ?? "Category");

            var skills = await context.Skills.AsNoTracking().ToListAsync();
            var skillLookup = skills
                .GroupBy(s => s.SkillsId)
                .ToDictionary(g => g.Key, g => g.First().SkillName ?? "Skill");

            var clientSkillScores = (await context.ReviewSkillRatings.AsNoTracking()
                .Where(rsr => rsr.Review.ReviewerRole == "Client"
                              && rsr.Review.Interview != null
                              && rsr.Review.Interview.WorkerId == workerId)
                .Select(rsr => new { rsr.SkillsId, rsr.Score })
                .ToListAsync())
                .GroupBy(x => x.SkillsId)
                .ToDictionary(g => g.Key, g => g.Select(x => x.Score).ToList());

            int? primaryCatId = junctionData.Where(r => r.IsPrimary).Select(r => (int?)r.CategoryId).FirstOrDefault()
                             ?? junctionData.Select(r => (int?)r.CategoryId).FirstOrDefault();

            result.PrimaryCategoryId = primaryCatId;
            if (primaryCatId.HasValue && categoryLookup.TryGetValue(primaryCatId.Value, out var pName))
                result.PrimaryRole = pName;

            var primarySkillIds = new List<int>();
            var partTimeSkillIdsByCategory = new Dictionary<int, List<int>>();

            foreach (var item in junctionData)
            {
                if (categoryLookup.TryGetValue(item.CategoryId, out var cName) && !result.Categories.Contains(cName))
                    result.Categories.Add(cName);

                if (item.CategoryId == primaryCatId)
                {
                    if (!primarySkillIds.Contains(item.SkillsId))
                        primarySkillIds.Add(item.SkillsId);
                    if (skillLookup.TryGetValue(item.SkillsId, out var sName) && !result.PrimarySkills.Contains(sName))
                        result.PrimarySkills.Add(sName);
                }
                else if (categoryLookup.ContainsKey(item.CategoryId))
                {
                    if (!partTimeSkillIdsByCategory.TryGetValue(item.CategoryId, out var list))
                    {
                        list = new List<int>();
                        partTimeSkillIdsByCategory[item.CategoryId] = list;
                    }
                    if (!list.Contains(item.SkillsId))
                        list.Add(item.SkillsId);
                }
            }

            List<SkillRatingRowVM> BuildRows(IEnumerable<int> skillIds)
            {
                var rows = new List<SkillRatingRowVM>();
                foreach (var skillId in skillIds.Distinct()
                             .OrderBy(id => skillLookup.TryGetValue(id, out var n) ? n : "")
                             .ThenBy(id => id))
                {
                    skillLookup.TryGetValue(skillId, out var name);
                    var scores = clientSkillScores.TryGetValue(skillId, out var saved) ? saved : new List<int>();
                    double? avg = scores.Count > 0 ? Math.Round(scores.Average(), 1) : null;
                    rows.Add(new SkillRatingRowVM
                    {
                        SkillsId = skillId,
                        Name = name ?? "Skill",
                        AverageRating = avg,
                        RatingCount = scores.Count
                    });
                }
                return rows;
            }

            (double? Average, int Count) SummarizeCategory(IEnumerable<int> skillIds)
            {
                var scores = skillIds.Distinct()
                    .Where(clientSkillScores.ContainsKey)
                    .SelectMany(id => clientSkillScores[id])
                    .ToList();
                double? avg = scores.Count > 0 ? Math.Round(scores.Average(), 1) : null;
                return (avg, scores.Count);
            }

            result.PrimarySkillRatings = BuildRows(primarySkillIds);
            var primarySummary = SummarizeCategory(primarySkillIds);
            result.PrimaryCategoryAverageRating = primarySummary.Average;
            result.PrimaryCategoryRatingCount = primarySummary.Count;

            foreach (var group in partTimeSkillIdsByCategory
                         .OrderBy(g => categoryLookup.TryGetValue(g.Key, out var n) ? n : ""))
            {
                categoryLookup.TryGetValue(group.Key, out var catName);
                var catSummary = SummarizeCategory(group.Value);
                result.PartTimeSkills.Add(new PartTimeSkillGroupVM
                {
                    CategoryId = group.Key,
                    CategoryName = catName ?? "Additional Service",
                    Skills = group.Value
                        .Select(id => skillLookup.TryGetValue(id, out var n) ? n : "Skill")
                        .Distinct()
                        .ToList(),
                    SkillRatings = BuildRows(group.Value),
                    AverageRating = catSummary.Average,
                    RatingCount = catSummary.Count
                });
            }

            return result;
        }

        public static async Task<List<WorkerCertificateRecordVM>> LoadWorkerCertificatesAsync(
            Fyp1Context context, int workerId)
        {
            var worker = await context.Workers.AsNoTracking()
                .Where(w => w.WorkerId == workerId)
                .Select(w => new { w.Name, w.Picture })
                .FirstOrDefaultAsync();

            var companyCerts = await context.WorkerCertifications.AsNoTracking()
                .Where(c => c.WorkerID == workerId)
                .Select(c => new
                {
                    CertificateId = c.CertificationID,
                    CertificateTitle = c.CertificateTitle,
                    EvaluationNotes = c.TrainingEvaluationNotes,
                    IssuedDate = c.IssuedDate,
                    CompanyName = c.Company != null ? c.Company.CompanyName : "Training Provider"
                })
                .ToListAsync();

            var courseCerts = await context.CourseCertificates.AsNoTracking()
                .Where(c => c.WorkerID == workerId && !c.IsRevoked)
                .Select(c => new
                {
                    CertificateId = c.CertificateID,
                    CertificateTitle = c.Course.CourseName,
                    CourseDescription = c.Course.Description,
                    Syllabus = c.Course.Syllabus,
                    IssuedDate = c.IssuedDateUtc,
                    CompanyName = c.Company.CompanyName,
                    CourseStartDate = (DateOnly?)c.Course.StartDate,
                    CourseEndDate = (DateOnly?)c.Course.EndDate,
                    CompletionDateUtc = c.Enrollment.CompletionDateUtc,
                    CertificateCode = c.CertificateCode,
                    CertificateUrl = c.CertificateUrl
                })
                .ToListAsync();

            var records = new List<(DateTime SortDate, WorkerCertificateRecordVM Record)>();

            foreach (var c in companyCerts)
            {
                records.Add((c.IssuedDate, new WorkerCertificateRecordVM
                {
                    CertificateId = c.CertificateId,
                    WorkerId = workerId,
                    WorkerName = worker?.Name ?? "Worker",
                    WorkerPicture = worker?.Picture,
                    CertificateType = "CompanyCertification",
                    CertificateTitle = string.IsNullOrWhiteSpace(c.CertificateTitle) ? "Professional Certification" : c.CertificateTitle,
                    CompanyName = string.IsNullOrWhiteSpace(c.CompanyName) ? "Training Provider" : c.CompanyName,
                    EvaluationNotes = c.EvaluationNotes,
                    IssuedDate = c.IssuedDate.ToString("dd MMM yyyy")
                }));
            }

            foreach (var c in courseCerts)
            {
                int? durationDays = null;
                if (c.CourseStartDate.HasValue && c.CourseEndDate.HasValue)
                {
                    var diff = c.CourseEndDate.Value.DayNumber - c.CourseStartDate.Value.DayNumber + 1;
                    if (diff > 0) durationDays = diff;
                }

                var syllabusItems = string.IsNullOrWhiteSpace(c.Syllabus)
                    ? new List<string>()
                    : c.Syllabus
                        .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .Select(line => line.TrimStart('-', '•', '*', ' '))
                        .Where(line => !string.IsNullOrWhiteSpace(line))
                        .ToList();

                records.Add((c.IssuedDate, new WorkerCertificateRecordVM
                {
                    CertificateId = c.CertificateId,
                    WorkerId = workerId,
                    WorkerName = worker?.Name ?? "Worker",
                    WorkerPicture = worker?.Picture,
                    CertificateType = "CourseCompletion",
                    CertificateTitle = string.IsNullOrWhiteSpace(c.CertificateTitle) ? "Course Completion" : c.CertificateTitle,
                    CompanyName = string.IsNullOrWhiteSpace(c.CompanyName) ? "Training Provider" : c.CompanyName,
                    CourseDescription = c.CourseDescription,
                    Syllabus = c.Syllabus,
                    SyllabusItems = syllabusItems,
                    IssuedDate = c.IssuedDate.ToString("dd MMM yyyy"),
                    CourseStartDate = c.CourseStartDate?.ToString("dd MMM yyyy"),
                    CourseEndDate = c.CourseEndDate?.ToString("dd MMM yyyy"),
                    DurationDays = durationDays,
                    CompletionDate = c.CompletionDateUtc?.ToString("dd MMM yyyy"),
                    CertificateCode = c.CertificateCode,
                    CertificateUrl = c.CertificateUrl
                }));
            }

            return records
                .OrderByDescending(r => r.SortDate)
                .Select(r => r.Record)
                .ToList();
        }
    }
}
