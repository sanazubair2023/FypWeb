namespace Fyp_Backend.Models;

// Snapshots are intentionally not foreign-keyed to interviews/hirings/terminations:
// deleting an old source record must not erase a worker's notification history.
public class WorkerNotification
{
    public int WorkerId { get; set; }
    public string NotificationKey { get; set; } = null!;
    public string Type { get; set; } = null!;
    public int SourceId { get; set; }
    public int? InterviewId { get; set; }
    public string Title { get; set; } = null!;
    public string Message { get; set; } = null!;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? ReadAtUtc { get; set; }
}
