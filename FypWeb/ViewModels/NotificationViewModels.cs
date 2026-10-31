namespace FypWeb.ViewModels;

public sealed class WorkerNotificationVM
{
    public string Key { get; set; } = "";
    public string Type { get; set; } = "";
    public string Title { get; set; } = "";
    public string Message { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? ReadAtUtc { get; set; }
    public string? DestinationUrl { get; set; }
    public bool IsRead => ReadAtUtc.HasValue;
}

public sealed class WorkerNotificationPageVM
{
    public List<WorkerNotificationVM> Items { get; set; } = new();
    public int UnreadCount { get; set; }
    public int Skip { get; set; }
    public int Take { get; set; }
    public bool HasMore { get; set; }
    public DateTime SnapshotAtUtc { get; set; }
}

public sealed class WorkerResignationReviewVM
{
    public int ResignationId { get; set; }
    public int InterviewId { get; set; }
    public string ClientName { get; set; } = "Client";
    public string? ClientPicture { get; set; }
    public string Reason { get; set; } = "";
    public string SubmittedDate { get; set; } = "";
    public string LastWorkingDate { get; set; } = "";
    public bool IsAccepted { get; set; }
    public string? AcceptanceMode { get; set; }
    public DateTime? AcceptedAtUtc { get; set; }
    public bool CanReview { get; set; }
    public ReviewVM? ClientReview { get; set; }
    public ReviewVM? WorkerReview { get; set; }
}
