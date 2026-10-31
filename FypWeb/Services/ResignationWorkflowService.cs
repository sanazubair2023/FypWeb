using System.Data;
using Fyp_Backend.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace FypWeb.Services;

public sealed class ResignationWorkflowException(int statusCode, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}

public sealed class ResignationWorkflowService(Fyp1Context db, TimeProvider clock)
{
    public const int NoticeDays = 7;
    // For non-relational test stores only. SQL Server uses transaction-held row
    // locks, not a process-local lock, so separate API servers share the guard.
    private static readonly SemaphoreSlim TestStoreGate = new(1, 1);
    public DateTime UtcNow => clock.GetUtcNow().UtcDateTime;
    public static DateTime BusinessLocal(DateTime utc) =>
        DateTime.SpecifyKind(utc.AddHours(5), DateTimeKind.Unspecified);
    public static DateOnly BusinessDate(DateTime utc) => DateOnly.FromDateTime(BusinessLocal(utc));
    public static bool IsStatus(string? value, string status) =>
        string.Equals(value?.Trim(), status, StringComparison.OrdinalIgnoreCase);
    public static bool IsAccepted(Resignation r) => IsStatus(r.Interview?.Status, "Resigned") &&
        (r.AcceptedAtUtc != null || r.NoticeEndsAtUtc == null);
    public static bool IsEnded(string? status) => IsStatus(status, "Resigned") ||
        IsStatus(status, "Terminated") || IsStatus(status, "Completed");

    public async Task<WorkflowUnit> BeginUnitAsync(CancellationToken ct = default)
    {
        if (db.Database.IsRelational())
            return new WorkflowUnit(await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct), null);
        await TestStoreGate.WaitAsync(ct);
        return new WorkflowUnit(null, TestStoreGate);
    }

    /// <summary>
    /// Hide or restore a recurring Part-Time slot without deleting it. The worker
    /// and client availability endpoints both filter on this flag.
    /// </summary>
    public async Task SetPartTimeSlotActiveAsync(Interview interview, bool isActive, CancellationToken ct = default)
    {
        if (!string.Equals(interview.JobType, "Part-Time", StringComparison.OrdinalIgnoreCase) ||
            !interview.SlotId.HasValue || !interview.WorkerId.HasValue)
        {
            return;
        }

        var slotId = interview.SlotId.Value;
        var workerId = interview.WorkerId.Value;
        var slot = db.Database.IsSqlServer()
            ? await db.WorkerTimeSlots.FromSqlInterpolated($"SELECT * FROM [dbo].[WorkerTimeSlots] WITH (UPDLOCK, ROWLOCK) WHERE [Id] = {slotId} AND [WorkerId] = {workerId}").FirstOrDefaultAsync(ct)
            : await db.WorkerTimeSlots.FirstOrDefaultAsync(s => s.Id == slotId && s.WorkerId == workerId, ct);

        if (slot != null) slot.IsActive = isActive;
    }

    // All writers lock the interview first, then its resignation. Consistent
    // ordering serializes submission, expiry, manual acceptance and reviews.
    public Task<Interview?> LockInterviewAsync(int interviewId, CancellationToken ct = default)
    {
        var query = db.Database.IsSqlServer()
            ? db.Interviews.FromSqlInterpolated($"SELECT * FROM [dbo].[Interview] WITH (UPDLOCK, ROWLOCK) WHERE [Interview_ID] = {interviewId}")
            : db.Interviews.Where(i => i.InterviewId == interviewId);
        return query.FirstOrDefaultAsync(ct);
    }

    private Task<Resignation?> LockNoticeAsync(int id, CancellationToken ct)
    {
        var query = db.Database.IsSqlServer()
            ? db.Resignations.FromSqlInterpolated($"SELECT * FROM [dbo].[Resignation] WITH (UPDLOCK, ROWLOCK) WHERE [Resignation_ID] = {id}")
            : db.Resignations.Where(r => r.ResignationId == id);
        return query.Include(r => r.Interview).FirstOrDefaultAsync(ct);
    }

    public async Task<Resignation> SubmitAsync(int workerId, int interviewId, string? reason, CancellationToken ct = default)
    {
        if (interviewId <= 0 || string.IsNullOrWhiteSpace(reason))
            throw new ResignationWorkflowException(400, "A job and resignation reason are required.");
        if (reason.Trim().Length > 4000)
            throw new ResignationWorkflowException(400, "The resignation reason must be 4,000 characters or fewer.");
        await using var unit = await BeginUnitAsync(ct);
        var interview = await LockInterviewAsync(interviewId, ct);
        if (interview == null || interview.WorkerId != workerId)
            throw new ResignationWorkflowException(404, "Job record not found.");
        if (IsEnded(interview.Status))
            throw new ResignationWorkflowException(409, "This job has already ended.");
        if (IsStatus(interview.Status, "ResignationPending") ||
            await db.Resignations.AnyAsync(r => r.InterviewId == interviewId, ct))
            throw new ResignationWorkflowException(409, "A resignation already exists for this job.");
        var hired = await db.Hiring.AnyAsync(h => h.InterviewId == interviewId && h.HiringDecision == "Accepted", ct);
        if (!hired && !new[] { "Approved", "Finalized", "Hired", "Accepted" }.Any(s => IsStatus(interview.Status, s)))
            throw new ResignationWorkflowException(409, "Only an active job can be resigned from.");
        var now = UtcNow;
        var end = now.AddDays(NoticeDays);
        var notice = new Resignation
        {
            InterviewId = interviewId,
            ResignationReason = reason.Trim(),
            SubmittedDate = BusinessLocal(now),
            LastWorkingDate = BusinessDate(end),
            NoticeEndsAtUtc = end
        };
        db.Resignations.Add(notice);
        interview.Status = "ResignationPending";
        // The worker requested immediate reactivation on resignation submission,
        // even though the contract remains in its notice period.
        await SetPartTimeSlotActiveAsync(interview, true, ct);
        // Persist the resignation first. For an exclusive full-time contract,
        // resignation submission releases its availability lock immediately.
        await db.SaveChangesAsync(ct);
        if (string.Equals(interview.JobType, "Full-Time", StringComparison.OrdinalIgnoreCase) && interview.IsExclusive)
        {
            // Finalization locks interview -> worker. Use that same order here so
            // a concurrent exclusive hire cannot overwrite this availability release.
            var worker = await new ContractRules(db).LockWorkerAsync(workerId);
            var anotherExclusiveJob = await db.Hiring.AnyAsync(h => h.HiringDecision == "Accepted"
                && h.Interview != null && h.Interview.WorkerId == workerId
                && h.Interview.InterviewId != interviewId
                && h.Interview.JobType == "Full-Time" && h.Interview.IsExclusive
                && h.Interview.Status != "Resigned" && h.Interview.Status != "Terminated"
                && h.Interview.Status != "Completed"
                && !db.Resignations.Any(r => r.InterviewId == h.InterviewId), ct);
            if (worker != null) worker.AvailableStatus = !anotherExclusiveJob;
            await db.SaveChangesAsync(ct);
        }
        await unit.CommitAsync(ct);
        return notice;
    }

    public async Task<Resignation> ConfirmWithClientReviewAsync(int clientId, int interviewId, int? resignationId,
        Func<Task<Review>> prepareReview, CancellationToken ct = default)
    {
        await using var unit = await BeginUnitAsync(ct);
        var interview = await LockInterviewAsync(interviewId, ct);
        if (interview == null || interview.ClientId != clientId)
            throw new ResignationWorkflowException(404, "Job record not found.");
        var id = await db.Resignations.AsNoTracking().Where(r => r.InterviewId == interviewId &&
                (!resignationId.HasValue || r.ResignationId == resignationId.Value))
            .OrderByDescending(r => r.SubmittedDate).ThenByDescending(r => r.ResignationId)
            .Select(r => (int?)r.ResignationId).FirstOrDefaultAsync(ct);
        if (id == null) throw new ResignationWorkflowException(404, "No resignation is on record for this contract.");
        var notice = (await LockNoticeAsync(id.Value, ct))!;
        if (IsStatus(interview.Status, "Terminated") || IsStatus(interview.Status, "Completed"))
            throw new ResignationWorkflowException(409, "This contract has already ended outside this resignation.");
        var alreadyReviewed = await db.Reviews.AnyAsync(r => r.InterviewId == interviewId && r.ReviewerRole == "Client", ct);
        var accepted = IsAccepted(notice);
        if (accepted && alreadyReviewed)
            throw new ResignationWorkflowException(400, "This resignation is already accepted and your review is saved.");
        if (!alreadyReviewed)
        {
            var review = await prepareReview(); // Criteria and remarks remain required.
            review.InterviewId = interviewId;
            review.ReviewerRole = "Client";
            review.ReviewDate = BusinessLocal(UtcNow);
            db.Reviews.Add(review);
        }
        if (!accepted)
        {
            var now = UtcNow;
            var automatic = notice.NoticeEndsAtUtc.HasValue && notice.NoticeEndsAtUtc.Value <= now;
            ApplyAcceptance(notice, interview, automatic ? notice.NoticeEndsAtUtc!.Value : now,
                automatic ? "Automatic" : "Client");
        }
        // Keep the slot active when the resignation takes effect as well, in
        // case another Part-Time finalization hid it during the notice period.
        await SetPartTimeSlotActiveAsync(interview, true, ct);
        await EnsureAcceptanceNoticeAsync(notice, interview, ct);
        await db.SaveChangesAsync(ct);
        await unit.CommitAsync(ct);
        return notice;
    }

    public async Task<int> ProcessDueAsync(int? clientId = null, int? workerId = null,
        int batchSize = 200, CancellationToken ct = default)
    {
        var now = UtcNow;
        var query = db.Resignations.AsNoTracking().Where(r => r.NoticeEndsAtUtc != null &&
            r.NoticeEndsAtUtc <= now && r.AcceptedAtUtc == null && r.Interview != null &&
            r.Interview.Status == "ResignationPending");
        if (clientId.HasValue) query = query.Where(r => r.Interview!.ClientId == clientId.Value);
        if (workerId.HasValue) query = query.Where(r => r.Interview!.WorkerId == workerId.Value);
        var due = await query.OrderBy(r => r.NoticeEndsAtUtc).ThenBy(r => r.ResignationId)
            .Take(Math.Clamp(batchSize, 1, 1000)).Select(r => new { r.ResignationId, r.InterviewId }).ToListAsync(ct);
        var accepted = 0;
        foreach (var item in due)
        {
            if (item.InterviewId == null) continue;
            await using var unit = await BeginUnitAsync(ct);
            var interview = await LockInterviewAsync(item.InterviewId.Value, ct);
            var notice = await LockNoticeAsync(item.ResignationId, ct);
            // Recheck under the same write lock. Never overwrite termination,
            // another acceptance, or a non-pending/legacy record.
            if (interview == null || notice == null || notice.NoticeEndsAtUtc == null ||
                notice.AcceptedAtUtc != null || notice.NoticeEndsAtUtc > UtcNow ||
                !IsStatus(interview.Status, "ResignationPending")) continue;
            ApplyAcceptance(notice, interview, notice.NoticeEndsAtUtc.Value, "Automatic");
            await SetPartTimeSlotActiveAsync(interview, true, ct);
            await EnsureAcceptanceNoticeAsync(notice, interview, ct);
            await db.SaveChangesAsync(ct);
            await unit.CommitAsync(ct);
            accepted++;
        }
        return accepted;
    }

    public static WorkerNotification AcceptanceNotice(int workerId, int resignationId, int? interviewId,
        string? mode, bool hasReview, DateTime createdAtUtc)
    {
        var automatic = mode == "Automatic";
        return new WorkerNotification
        {
            WorkerId = workerId,
            NotificationKey = $"resignation:{resignationId}:accepted",
            Type = "resignation",
            SourceId = resignationId,
            InterviewId = interviewId,
            CreatedAtUtc = createdAtUtc,
            Title = automatic ? "Resignation automatically accepted" : "Resignation accepted",
            Message = hasReview ? "Your resignation is accepted. Open your saved client review."
                : automatic ? "Your seven-day notice period ended. Your resignation was automatically accepted. You can now review your client."
                : "Your client accepted your resignation. You can now review your client."
        };
    }

    private async Task EnsureAcceptanceNoticeAsync(Resignation notice, Interview interview, CancellationToken ct)
    {
        if (interview.WorkerId is not int workerId) return;
        var key = $"resignation:{notice.ResignationId}:accepted";
        if (await db.WorkerNotifications.AnyAsync(n => n.WorkerId == workerId && n.NotificationKey == key, ct)) return;
        var hasReview = await db.Reviews.AnyAsync(r => r.InterviewId == interview.InterviewId && r.ReviewerRole == "Worker", ct);
        // Queued in the SAME transaction as acceptance, even with both apps
        // closed. The normal bell importer also repairs/backfills missing rows.
        db.WorkerNotifications.Add(AcceptanceNotice(workerId, notice.ResignationId,
            notice.InterviewId, notice.AcceptanceMode, hasReview, DateTime.UtcNow));
    }

    private static void ApplyAcceptance(Resignation notice, Interview interview, DateTime effectiveUtc, string mode)
    {
        notice.AcceptedAtUtc = DateTime.SpecifyKind(effectiveUtc, DateTimeKind.Utc);
        notice.AcceptanceMode = mode;
        interview.Status = "Resigned";
        // Keep the original seven-day deadline/proposed last day for audit;
        // worked-period rendering uses AcceptedAtUtc as the actual ending.
    }

    public async Task<Review> SubmitWorkerReviewAsync(int workerId, int interviewId, decimal? rating,
        string? comment, CancellationToken ct = default)
    {
        if (rating is null or < 1 or > 5 || decimal.Truncate(rating.Value) != rating.Value)
            throw new ResignationWorkflowException(400, "Choose a whole-number star rating from 1 to 5.");
        await using var unit = await BeginUnitAsync(ct);
        var interview = await LockInterviewAsync(interviewId, ct);
        if (interview == null || interview.WorkerId != workerId)
            throw new ResignationWorkflowException(404, "Job record not found.");
        var acceptedResignation = IsStatus(interview.Status, "Resigned") &&
            await db.Resignations.AnyAsync(r => r.InterviewId == interviewId &&
                (r.AcceptedAtUtc != null || r.NoticeEndsAtUtc == null), ct);
        if (!acceptedResignation && !IsStatus(interview.Status, "Terminated"))
            throw new ResignationWorkflowException(409, "You can review the client only after the resignation is accepted or the contract is terminated.");
        if (await db.Reviews.AnyAsync(r => r.InterviewId == interviewId && r.ReviewerRole == "Worker", ct))
            throw new ResignationWorkflowException(400, "You have already reviewed this client for this contract.");
        var review = new Review
        {
            InterviewId = interviewId,
            Rating = rating.Value,
            Comment = comment?.Trim(),
            ReviewerRole = "Worker",
            ReviewDate = BusinessLocal(UtcNow)
        };
        db.Reviews.Add(review);
        await db.SaveChangesAsync(ct);
        await unit.CommitAsync(ct);
        return review;
    }

    public sealed class WorkflowUnit(IDbContextTransaction? transaction, SemaphoreSlim? gate) : IAsyncDisposable
    {
        public Task CommitAsync(CancellationToken ct) => transaction?.CommitAsync(ct) ?? Task.CompletedTask;
        public async ValueTask DisposeAsync()
        {
            if (transaction != null) await transaction.DisposeAsync();
            gate?.Release();
        }
    }
}
