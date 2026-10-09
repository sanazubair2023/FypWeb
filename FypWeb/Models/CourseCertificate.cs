using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Fyp_Backend.Models;

[Table("CourseCertificates")]
public class CourseCertificate
{
    [Key] public int CertificateID { get; set; }
    public int EnrollmentID { get; set; }
    public int CourseID { get; set; }
    public int WorkerID { get; set; }
    public int CompanyID { get; set; }
    [MaxLength(50)] public string CertificateCode { get; set; } = null!;
    public DateTime IssuedDateUtc { get; set; }
    [MaxLength(500)] public string? CertificateUrl { get; set; }
    public bool IsRevoked { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
    [MaxLength(500)] public string? RevocationReason { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public CourseEnrollment Enrollment { get; set; } = null!;
    public Course Course { get; set; } = null!;
    public Worker Worker { get; set; } = null!;
    public Company Company { get; set; } = null!;
}
