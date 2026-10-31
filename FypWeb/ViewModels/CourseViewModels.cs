using System.ComponentModel.DataAnnotations;

namespace FypWeb.ViewModels
{
    // ── COMPANY DASHBOARD (RN CompanyDashboardScreen / GET api/company/dashboard) ──
    public class CompanyDashboardVM
    {
        public int Id { get; set; }
        public string CompanyName { get; set; } = "";
        public string? LicenseNumber { get; set; }
        public string? CompanyAddress { get; set; }
        public string? Picture { get; set; }
        public int TotalCourses { get; set; }
        public int ActiveCourses { get; set; }
        public int TotalEnrollments { get; set; }
        public int UniqueEnrolledWorkers { get; set; }
        public int CompletedEnrollments { get; set; }
    }

    // ── COMPANY COURSE LIST (scope tabs active | past | archived) ─────────────────
    public class CourseCardVM
    {
        public int CourseId { get; set; }
        public string CourseName { get; set; } = "";
        public string StartDate { get; set; } = "";
        public string EndDate { get; set; } = "";
        public string ClassTimings { get; set; } = "";
        public string? Description { get; set; }
        public bool IsPublished { get; set; }
        public bool IsDeleted { get; set; }
        public int EnrollmentCount { get; set; }
        public int CompletedCount { get; set; }
    }

    // ── COURSE CREATE / EDIT FORM (RN CompanyCourseFormScreen) ───────────────────
    public class CourseFormVM
    {
        public int CourseId { get; set; } // 0 = create
        [Required, MaxLength(150)]
        public string CourseName { get; set; } = "";
        public string StartDate { get; set; } = ""; // yyyy-MM-dd
        public string EndDate { get; set; } = "";
        public string StartTime { get; set; } = "09:00"; // HH:mm
        public string EndTime { get; set; } = "17:00";
        public string? Description { get; set; }
        public string? Syllabus { get; set; }
        public bool IsPublished { get; set; } = true;
    }

    // ── COURSE DETAIL + ENROLLMENTS (RN CompanyCourseDetailScreen) ───────────────
    public class CourseDetailVM
    {
        public int CourseId { get; set; }
        public string CourseName { get; set; } = "";
        public string StartDate { get; set; } = "";
        public string EndDate { get; set; } = "";
        public string ClassTimings { get; set; } = "";
        public string? Description { get; set; }
        public string? Syllabus { get; set; }
        public bool IsPublished { get; set; }
        public bool IsDeleted { get; set; }
        public List<EnrollmentRowVM> Enrollments { get; set; } = new();
    }

    public class EnrollmentRowVM
    {
        public int EnrollmentId { get; set; }
        public int WorkerId { get; set; }
        public string WorkerName { get; set; } = "Worker";
        public string? WorkerPicture { get; set; }
        public string Status { get; set; } = "Enrolled";
        public string EnrolledAt { get; set; } = "";
        public int? CertificateId { get; set; }
        public string? CertificateCode { get; set; }
    }

    // ── WORKER CATALOGUE (RN WorkerCourseCatalogueScreen) ────────────────────────
    public class WorkerCourseCardVM
    {
        public int CourseId { get; set; }
        public string CourseName { get; set; } = "";
        public string CompanyName { get; set; } = "";
        public string StartDate { get; set; } = "";
        public string EndDate { get; set; } = "";
        public string ClassTimings { get; set; } = "";
        public string? Description { get; set; }
        public string? EnrollmentStatus { get; set; } // null = not enrolled
    }

    // ── WORKER MY COURSES (RN WorkerMyCoursesScreen) ─────────────────────────────
    public class MyCourseVM
    {
        public int EnrollmentId { get; set; }
        public string Status { get; set; } = "Enrolled";
        public string CourseName { get; set; } = "";
        public string CompanyName { get; set; } = "";
        public string StartDate { get; set; } = "";
        public string EndDate { get; set; } = "";
        public string ClassTimings { get; set; } = "";
        public int? CertificateId { get; set; }
        public string? CertificateCode { get; set; }
    }

    // ── DIGITAL CERTIFICATE (RN CourseCertificateScreen) ─────────────────────────
    public class CertificateVM
    {
        public int CertificateId { get; set; }
        public string CertificateCode { get; set; } = "";
        public string WorkerName { get; set; } = "";
        public string CompanyName { get; set; } = "";
        public string CourseName { get; set; } = "";
        public string IssuedDate { get; set; } = "";
        public bool IsValid { get; set; }
    }
}
