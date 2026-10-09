using Fyp_Backend.Models;
using Microsoft.EntityFrameworkCore;

namespace FypWeb.Helpers
{
    /// <summary>
    /// Shared logic for the criteria-based closing reviews
    /// (port of api Dashboard LoadContractWindowsAsync / FormatWorkedPeriod).
    /// </summary>
    public static class ReviewHelpers
    {
        // start = Hiring.HiringDate        (falls back to Interview.InterviewDate)
        // end   = Termination.TerminatedDate, else Resignation.LastWorkingDate
        //        (falls back to Resignation.SubmittedDate, then the review date)
        public static async Task<Dictionary<int, (DateTime? From, DateTime? To)>> LoadContractWindowsAsync(
            Fyp1Context context, List<int> interviewIds, Dictionary<int, DateTime?>? fallbackEndDates = null)
        {
            var windows = new Dictionary<int, (DateTime? From, DateTime? To)>();
            if (interviewIds == null || interviewIds.Count == 0) return windows;

            var hireDates = (await context.Hiring
                .Where(h => h.InterviewId != null && interviewIds.Contains(h.InterviewId.Value))
                .Select(h => new { InterviewId = h.InterviewId!.Value, h.HiringDate })
                .ToListAsync())
                .GroupBy(x => x.InterviewId)
                .ToDictionary(g => g.Key, g => g.Min(x => x.HiringDate));

            var interviewDates = (await context.Interviews
                .Where(i => interviewIds.Contains(i.InterviewId))
                .Select(i => new { i.InterviewId, i.InterviewDate })
                .ToListAsync())
                .ToDictionary(x => x.InterviewId, x => x.InterviewDate);

            var resignations = await context.Resignations
                .Where(r => r.InterviewId != null && interviewIds.Contains(r.InterviewId.Value))
                .Select(r => new { InterviewId = r.InterviewId!.Value, r.SubmittedDate, r.LastWorkingDate })
                .ToListAsync();

            var terminations = await context.Terminations
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
    }
}
