using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Fyp_Backend.Models;

[Table("CourseEnrollments")]
public class CourseEnrollment
{
    [Key] public int EnrollmentID { get; set; }
    public int CourseID { get; set; }
    public int WorkerID { get; set; }
    [MaxLength(20)] public string Status { get; set; } = "Enrolled";
    public DateTime EnrolledAtUtc { get; set; }
    public DateTime? CompletionDateUtc { get; set; }
    public DateTime? WithdrawnAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
    [Timestamp] public byte[] RowVersion { get; set; } = Array.Empty<byte>();
    public Course Course { get; set; } = null!;
    public Worker Worker { get; set; } = null!;
    public CourseCertificate? Certificate { get; set; }
}
