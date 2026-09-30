namespace FypWeb.ViewModels
{
    // ── Worker discovery / cards ──────────────────────────────────────────────
    public class WorkerCardVM
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string Role { get; set; } = "General";
        public string City { get; set; } = "";
        public string Salary { get; set; } = "";
        public string? Phone { get; set; }
        public string? Picture { get; set; }
        public string Rating { get; set; } = "0.0";
        public string Gender { get; set; } = "";
        public List<string> Categories { get; set; } = new();
        public bool AvailableStatus { get; set; }
        public bool IsPoliceVerified { get; set; }
    }

    public class ReviewVM
    {
        public int Rating { get; set; }
        public string? Comment { get; set; }
        public string ReviewerRole { get; set; } = "Client";
        public string ReviewDate { get; set; } = "";
    }

    public class WorkerDetailVM
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string? Cnic { get; set; }
        public string? Phone { get; set; }
        public string? Address { get; set; }
        public string? Picture { get; set; }
        public string? Gender { get; set; }
        public int? Age { get; set; }
        public string Salary { get; set; } = "";
        public string? Bio { get; set; }
        public double Rating { get; set; }
        public int ReviewCount { get; set; }
        public bool AvailableStatus { get; set; }
        public bool IsPoliceVerified { get; set; }
        public string JobType { get; set; } = "Full-Time";
        public double DistanceKm { get; set; }
        public double Radius { get; set; }
        public bool LocationKnown { get; set; }
        public List<string> Categories { get; set; } = new();
        public List<ExperienceVM> Experiences { get; set; } = new();
        public List<string> Habits { get; set; } = new();
        public List<SlotVM> Slots { get; set; } = new();
        public List<ReviewVM> Reviews { get; set; } = new();
        public bool HasActiveInterview { get; set; }

        // ── Trust & Verification (ported from RN WorkerDetailScreen) ──
        // Police character certificate
        public string PoliceCertStatus { get; set; } = "NotIssued"; // Verified | Expired | NotIssued
        public string? PoliceCertNo { get; set; }
        public string? PoliceCharacterStatus { get; set; }
        public string? PoliceIssuingStation { get; set; }
        public string? PoliceIssuingBadge { get; set; }
        public string? PoliceIssuedDate { get; set; }
        public string? PoliceExpiryDate { get; set; }
        public string? PoliceRemarks { get; set; }
        // Company-issued skill/training certificates
        public List<WorkerCertItemVM> CompanyCertificates { get; set; } = new();
    }

    public class WorkerCertItemVM
    {
        public string Title { get; set; } = "";
        public string? Notes { get; set; }
        public string CompanyName { get; set; } = "Training Provider";
        public string IssuedDate { get; set; } = "";
    }

    public class ExperienceVM
    {
        public string? WorkAt { get; set; }
        public string? ExpDetail { get; set; }
        public string? Duration { get; set; }
    }

    public class SlotVM
    {
        public int Id { get; set; }
        public string StartTime { get; set; } = "";
        public string EndTime { get; set; } = "";
        public bool IsTaken { get; set; }
    }

    public class BookInterviewVM
    {
        public int WorkerId { get; set; }
        public string WorkerName { get; set; } = "";
        public string? WorkerPicture { get; set; }
        public bool IsPartTime { get; set; }
        public bool HasSlots { get; set; }
        public double Radius { get; set; }
        public double DistanceKm { get; set; }
        public string ClientAddress { get; set; } = "";
        public List<SlotVM> Slots { get; set; } = new();
    }

    // ── Requests / hiring handshake ───────────────────────────────────────────
    public class InterviewRequestVM
    {
        public int InterviewId { get; set; }
        public int OtherPartyId { get; set; }
        public string OtherPartyName { get; set; } = "";
        public string? OtherPartyPicture { get; set; }
        public string Location { get; set; } = "";
        public string Time { get; set; } = "";
        public string Status { get; set; } = "";
        public string WorkerDecision { get; set; } = "";
        public string JobType { get; set; } = "Full-Time";
        public bool? IsResidenceProvided { get; set; }
        public int? HiringId { get; set; }
        public string HiringWorkerDecision { get; set; } = "";
        public string HiringDecision { get; set; } = "";
        public string? SlotStart { get; set; }   // <-- add
        public string? SlotEnd { get; set; }     // <-- add
    }

    // ── Resignations ──────────────────────────────────────────────────────────
    public class ResignationVM
    {
        public int ResignationId { get; set; }
        public int InterviewId { get; set; }
        public string WorkerName { get; set; } = "";
        public string? WorkerAvatar { get; set; }
        public string Reason { get; set; } = "";
        public string LastWorkingDate { get; set; } = "";
        public string SubmittedDate { get; set; } = "";
        public string Status { get; set; } = "";
    }

    // ── Worker dashboard ──────────────────────────────────────────────────────
    public class WorkerDashboardVM
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string? Cnic { get; set; }
        public string? Phone { get; set; }
        public string? Address { get; set; }
        public string? Picture { get; set; }
        public string? Gender { get; set; }
        public int? Age { get; set; }
        public string Salary { get; set; } = "";
        public string? Bio { get; set; }
        public bool AvailableStatus { get; set; }
        public int Radius { get; set; }
        public decimal? Latitude { get; set; }
        public decimal? Longitude { get; set; }
        public double Rating { get; set; }
        public int ReviewCount { get; set; }
        public List<string> Categories { get; set; } = new();
        public List<ExperienceVM> Experiences { get; set; } = new();
        public List<SlotVM> Slots { get; set; } = new();
        public List<ReviewVM> Reviews { get; set; } = new();
        public List<string> Habits { get; set; } = new();
    }

    // ── Map markers (client Location / Map screen) ────────────────────────────
    public class MapWorkerVM
    {
        public int Id { get; set; }
        public string Name { get; set; } = "Worker";
        public string Role { get; set; } = "Service Worker";
        public double Lat { get; set; }
        public double Lng { get; set; }
    }

    public class ActiveJobVM
    {
        public int InterviewId { get; set; }
        public string ClientName { get; set; } = "";
        public string? ClientPicture { get; set; }
        public string Address { get; set; } = "";
        public string JobType { get; set; } = "";
        public string Status { get; set; } = "";
    }

    // ── Company / directory ───────────────────────────────────────────────────
    public class CompanyWorkerVM
    {
        public int WorkerId { get; set; }
        public string Name { get; set; } = "";
        public string RoleTitle { get; set; } = "";
        public string Location { get; set; } = "";
        public string? Picture { get; set; }
        public string Salary { get; set; } = "";
        public string Rating { get; set; } = "4.0";
        public List<string> Categories { get; set; } = new();
    }

    public class WorkerCertificateVM
    {
        public int WorkerId { get; set; }
        public string Name { get; set; } = "";
        public string? Cnic { get; set; }
        public string? Phone { get; set; }
        public string? Address { get; set; }
        public string? Picture { get; set; }
        public string RoleTitle { get; set; } = "";
        public bool HasCertificate { get; set; }
        public string? CertificateTitle { get; set; }
        public string? EvaluationNotes { get; set; }
        public string? IssuedDate { get; set; }
        public string? CompanyName { get; set; }
    }

    // ── Police / character certificate ────────────────────────────────────────
    public class PoliceWorkerVM
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string Category { get; set; } = "";
        public string? Cnic { get; set; }
        public string? Picture { get; set; }
        public bool IsCertified { get; set; }
        public string? CertExpiry { get; set; }
    }

    public class CharacterCertificateVM
    {
        public int WorkerId { get; set; }
        public string Name { get; set; } = "";
        public string? Cnic { get; set; }
        public string? Picture { get; set; }
        public bool HasValidCertificate { get; set; }
        public string Status { get; set; } = "NotIssued";
        public string? CertificateNo { get; set; }
        public string? CharacterStatus { get; set; }
        public string? Remarks { get; set; }
        public string? IssuingStation { get; set; }
        public string? IssuingBadge { get; set; }
        public string? IssuedDate { get; set; }
        public string? ExpiryDate { get; set; }
    }
}
