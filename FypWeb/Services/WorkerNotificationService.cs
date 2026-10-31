using Fyp_Backend.Models;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace FypWeb.Services;

public sealed class WorkerNotificationService(Fyp1Context db, ResignationWorkflowService workflow)
{
    // Existing source tables do not have reliable event-created timestamps.
    // CreatedAtUtc records when this center first observes a source event, not
    // the scheduled interview date. Stable keys prevent duplicates on refresh.
    public async Task SynchronizeAsync(int workerId, CancellationToken cancellationToken = default)
    {
        await workflow.ProcessDueAsync(workerId: workerId, ct: cancellationToken);
        var observedAt = DateTime.UtcNow;
        var candidates = new List<WorkerNotification>();
        var interviews = await db.Interviews.AsNoTracking()
            .Where(i => i.WorkerId == workerId)
            .Select(i => new { i.InterviewId, i.InterviewDate, ClientName = i.Client != null ? i.Client.Name : null })
            .ToListAsync(cancellationToken);
        foreach (var i in interviews)
        {
            var name = string.IsNullOrWhiteSpace(i.ClientName) ? "A client" : i.ClientName;
            var when = i.InterviewDate.HasValue ? $" Interview: {i.InterviewDate.Value:dd MMM yyyy, hh:mm tt}." : "";
            candidates.Add(New($"interview:{i.InterviewId}", "interview", i.InterviewId,
                i.InterviewId, "Interview request", $"{name} requested an interview with you.{when}"));
        }

        var jobs = await db.Hiring.AsNoTracking()
            .Where(h => h.Interview != null && h.Interview.WorkerId == workerId)
            .Select(h => new
            {
                h.HiringId,
                h.InterviewId,
                h.HiringDecision,
                ClientName = h.Interview!.Client != null ? h.Interview.Client.Name : null
            }).ToListAsync(cancellationToken);
        foreach (var h in jobs)
        {
            var name = string.IsNullOrWhiteSpace(h.ClientName) ? "The client" : h.ClientName;
            var decision = h.HiringDecision?.Trim().ToLowerInvariant();
            var stage = decision == "accepted" ? "confirmed" : decision == "rejected" ? "rejected" : "offer";
            var title = stage == "confirmed" ? "Job confirmed" : stage == "rejected" ? "Job application update" : "Job offer";
            var message = stage == "confirmed" ? $"{name} confirmed your job. Open the job details."
                : stage == "rejected" ? $"{name} decided not to proceed with this job."
                : $"{name} sent you a job offer. Open it to view the current status or respond.";
            // Confirmation/rejection is a separate event from an earlier offer.
            // The worker's own accept/reject action does not create another alert.
            candidates.Add(New($"job:{h.HiringId}:{stage}", "job", h.HiringId,
                h.InterviewId, title, message));
        }

        var terminations = await db.Terminations.AsNoTracking()
            .Where(t => t.Interview != null && t.Interview.WorkerId == workerId)
            .Select(t => new
            {
                t.TerminationId,
                t.InterviewId,
                t.TerminatedReason,
                ClientName = t.Interview!.Client != null ? t.Interview.Client.Name : null
            }).ToListAsync(cancellationToken);
        foreach (var t in terminations)
        {
            var name = string.IsNullOrWhiteSpace(t.ClientName) ? "The client" : t.ClientName;
            var reason = string.IsNullOrWhiteSpace(t.TerminatedReason) ? "Reason not provided." : t.TerminatedReason.Trim();
            candidates.Add(New($"termination:{t.TerminationId}", "termination", t.TerminationId,
                t.InterviewId, "Contract terminated", $"{name} ended your contract. {reason}"));
        }

        var acceptedNotices = await db.Resignations.AsNoTracking()
            .Where(r => r.Interview != null && r.Interview.WorkerId == workerId &&
                r.Interview.Status == "Resigned" && r.AcceptedAtUtc != null)
            .Select(r => new {
                r.ResignationId,
                r.InterviewId,
                r.AcceptanceMode,
                HasReview = db.Reviews.Any(review => review.InterviewId == r.InterviewId && review.ReviewerRole == "Worker")
            })
            .ToListAsync(cancellationToken);
        foreach (var r in acceptedNotices)
            candidates.Add(ResignationWorkflowService.AcceptanceNotice(workerId, r.ResignationId,
                r.InterviewId, r.AcceptanceMode, r.HasReview, observedAt));

        // Another device/server may discover the same events concurrently. The
        // composite PK is the final guard; retry only SQL duplicate-key failures.
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var known = (await db.WorkerNotifications.AsNoTracking()
                .Where(n => n.WorkerId == workerId).Select(n => n.NotificationKey)
                .ToListAsync(cancellationToken)).ToHashSet(StringComparer.Ordinal);
            var missing = candidates.Where(n => !known.Contains(n.NotificationKey)).ToList();
            if (missing.Count == 0) return;
            db.WorkerNotifications.AddRange(missing);
            try
            {
                await db.SaveChangesAsync(cancellationToken);
                return;
            }
            catch (DbUpdateException ex) when (attempt < 2 &&
                ex.InnerException is SqlException { Number: 2601 or 2627 })
            {
                foreach (var item in missing) db.Entry(item).State = EntityState.Detached;
            }
        }

        WorkerNotification New(string key, string type, int sourceId, int? interviewId, string title, string message)
            => new()
            {
                WorkerId = workerId,
                NotificationKey = key,
                Type = type,
                SourceId = sourceId,
                InterviewId = interviewId,
                Title = title,
                Message = message,
                CreatedAtUtc = observedAt
            };
    }
}
