using Fyp_Backend.Models;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace FypWeb.Services;

public sealed class WorkerNotificationService(Fyp1Context db, ResignationWorkflowService workflow)
{
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

        // Backfill Company Skill Evaluation Certificates (exact Fyp_Backend key: company-certificate:{CertificationID})
        var companyCerts = await db.WorkerCertifications.AsNoTracking()
            .Where(c => c.WorkerID == workerId)
            .Select(c => new
            {
                c.CertificationID,
                c.CertificateTitle,
                CompanyName = c.Company != null ? c.Company.CompanyName : null
            })
            .ToListAsync(cancellationToken);
        foreach (var cert in companyCerts)
        {
            var company = string.IsNullOrWhiteSpace(cert.CompanyName) ? "A verified company" : cert.CompanyName;
            var title = string.IsNullOrWhiteSpace(cert.CertificateTitle) ? "Skill Evaluation Certificate" : cert.CertificateTitle;
            candidates.Add(New($"company-certificate:{cert.CertificationID}", "company-certificate", workerId,
                null, "New skill certificate issued", $"{company} issued your \"{title}\" skill certificate."));
        }

        // Backfill Course Completion Certificates (exact Fyp_Backend key: course-certificate:{CertificateID})
        var courseCerts = await db.CourseCertificates.AsNoTracking()
            .Where(c => c.WorkerID == workerId && !c.IsRevoked)
            .Select(c => new
            {
                c.CertificateID,
                CourseTitle = c.Course != null ? c.Course.CourseName : null,
                CompanyName = c.Company != null ? c.Company.CompanyName : null
            })
            .ToListAsync(cancellationToken);
        foreach (var cc in courseCerts)
        {
            var company = string.IsNullOrWhiteSpace(cc.CompanyName) ? "Training company" : cc.CompanyName;
            var course = string.IsNullOrWhiteSpace(cc.CourseTitle) ? "training course" : cc.CourseTitle;
            candidates.Add(New($"course-certificate:{cc.CertificateID}", "course-certificate", cc.CertificateID,
                null, "Course certificate issued", $"{company} issued your certificate for completing \"{course}\"."));
        }

        // Backfill Police Character Certificate if worker has one and no police-certificate notification exists yet
        var policeCert = await db.PoliceRecords.AsNoTracking()
            .Where(pr => pr.WorkerID == workerId && !pr.IsRevoked)
            .OrderByDescending(pr => pr.IssuedDate)
            .Select(pr => new { pr.CertificateNo, pr.IssuingStation })
            .FirstOrDefaultAsync(cancellationToken);

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var known = (await db.WorkerNotifications.AsNoTracking()
                .Where(n => n.WorkerId == workerId).Select(n => n.NotificationKey)
                .ToListAsync(cancellationToken)).ToHashSet(StringComparer.Ordinal);

            var allCandidates = new List<WorkerNotification>(candidates);
            if (policeCert != null && !known.Any(k => k.StartsWith("police-certificate:", StringComparison.Ordinal)))
            {
                var station = string.IsNullOrWhiteSpace(policeCert.IssuingStation) ? "Police Verification Portal" : policeCert.IssuingStation;
                allCandidates.Add(New($"police-certificate:{workerId}:initial", "police-certificate", workerId,
                    null, "Police character certificate verified", $"{station} verified your character certificate ({policeCert.CertificateNo})."));
            }

            var missing = allCandidates.Where(n => !known.Contains(n.NotificationKey)).ToList();
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
