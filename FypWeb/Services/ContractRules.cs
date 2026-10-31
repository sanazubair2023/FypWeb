using Fyp_Backend.Models;
using Microsoft.EntityFrameworkCore;

namespace FypWeb.Services;

/// <summary>Shared hiring capacity and status rules for the self-contained web backend.</summary>
public sealed class ContractRules(Fyp1Context db)
{
    public static bool IsEnded(string? status) =>
        string.Equals(status, "Resigned", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(status, "Terminated", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(status, "Completed", StringComparison.OrdinalIgnoreCase);

    public Task<bool> HasActiveExclusiveFullTimeJobAsync(int workerId, int? excludingInterviewId = null) =>
        db.Hiring.AnyAsync(h => h.HiringDecision == "Accepted" && h.Interview != null
            && h.Interview.WorkerId == workerId
            && h.Interview.JobType == "Full-Time" && h.Interview.IsExclusive
            && h.Interview.Status != "Resigned" && h.Interview.Status != "Terminated" && h.Interview.Status != "Completed"
            && (!excludingInterviewId.HasValue || h.InterviewId != excludingInterviewId.Value)
            && !db.Resignations.Any(r => r.InterviewId == h.InterviewId));

    public Task<bool> HasActiveFullTimeJobAsync(int workerId, int? excludingInterviewId = null) =>
        db.Hiring.AnyAsync(h => h.HiringDecision == "Accepted" && h.Interview != null
            && h.Interview.WorkerId == workerId && h.Interview.JobType == "Full-Time"
            && h.Interview.Status != "Resigned" && h.Interview.Status != "Terminated" && h.Interview.Status != "Completed"
            && (!excludingInterviewId.HasValue || h.InterviewId != excludingInterviewId.Value)
            && !db.Resignations.Any(r => r.InterviewId == h.InterviewId));

    public IQueryable<Interview> ActiveSlotReservations(int workerId) =>
        db.Interviews.Where(i => i.WorkerId == workerId && i.SlotId != null &&
            i.Status != "Rejected" && i.Status != "JobRejected" && i.Status != "Terminated" &&
            i.Status != "Resigned" && i.Status != "ResignationPending" && i.Status != "Completed");

    /// <summary>
    /// A finalized exclusive full-time contract removes the worker from other
    /// clients' discovery and maps. The employer keeps visibility, while an
    /// ordinary offline/non-exclusive worker is not filtered by this rule.
    /// Resignation submission releases the restriction just like availability.
    /// </summary>
    public IQueryable<Worker> DiscoverableByClient(IQueryable<Worker> workers, int clientId) =>
        workers.Where(w => !db.Hiring.Any(h => h.HiringDecision == "Accepted" && h.Interview != null
            && h.Interview.WorkerId == w.WorkerId
            && (h.Interview.ClientId == null || h.Interview.ClientId != clientId)
            && h.Interview.JobType == "Full-Time" && h.Interview.IsExclusive
            && h.Interview.Status != "Resigned" && h.Interview.Status != "Terminated" && h.Interview.Status != "Completed"
            && !db.Resignations.Any(r => r.InterviewId == h.InterviewId)));

    public async Task ReleaseExclusiveAvailabilityAsync(Interview interview)
    {
        if (!string.Equals(interview.JobType, "Full-Time", StringComparison.OrdinalIgnoreCase) ||
            !interview.IsExclusive || interview.WorkerId is not int workerId) return;
        // Match finalization's interview -> worker lock order before checking
        // whether another exclusive contract still owns the availability lock.
        var worker = await LockWorkerAsync(workerId);
        var another = await HasActiveExclusiveFullTimeJobAsync(workerId, interview.InterviewId);
        if (worker != null) worker.AvailableStatus = !another;
    }

    public Task<Worker?> LockWorkerAsync(int workerId) =>
        db.Database.IsSqlServer()
            ? db.Workers.FromSqlInterpolated($"SELECT * FROM [dbo].[Worker] WITH (UPDLOCK, ROWLOCK) WHERE [Worker_ID] = {workerId}").FirstOrDefaultAsync()
            : db.Workers.FirstOrDefaultAsync(w => w.WorkerId == workerId);
}
