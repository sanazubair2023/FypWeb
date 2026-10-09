using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Fyp_Backend.Models;

[Table("Courses")]
public class Course
{
    [Key] public int CourseID { get; set; }
    public int CompanyID { get; set; }
    [MaxLength(150)] public string CourseName { get; set; } = null!;
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    [MaxLength(200)] public string ClassTimings { get; set; } = null!;
    public string? Description { get; set; }
    public string? Syllabus { get; set; }
    public bool IsPublished { get; set; } = true;
    public bool IsDeleted { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
    public DateTime? DeletedAtUtc { get; set; }
    [Timestamp] public byte[] RowVersion { get; set; } = Array.Empty<byte>();
    public Company Company { get; set; } = null!;
    public ICollection<CourseEnrollment> Enrollments { get; set; } = new List<CourseEnrollment>();
    public ICollection<CourseCertificate> Certificates { get; set; } = new List<CourseCertificate>();
}
