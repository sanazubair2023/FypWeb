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
        public bool IsCompanyCertified { get; set; }
    }

    public class ReviewVM
    {
        public decimal Rating { get; set; }
        public string? Comment { get; set; }
        public string ReviewerRole { get; set; } = "Client";
        public string ReviewerName { get; set; } = "Anonymous";
        public string ReviewDate { get; set; } = "";
        public string? WorkedPeriod { get; set; }
        public string? EndType { get; set; }
        public string? EndReasonType { get; set; }
        public string? EndReason { get; set; }
        public List<CriteriaScoreVM> Criteria { get; set; } = new();
        public List<CriteriaScoreVM> SkillRatings { get; set; } = new();
        public int? ReviewerClientId { get; set; }
        public int? ReviewerId { get; set; }
    }

    public class CriteriaScoreVM
    {
        public string Name { get; set; } = "";
        public int Score { get; set; }
    }

    // ── DETAIL-REVIEW INPUT (RN CriteriaReviewSection) ────────────────────────
    public class CriteriaInputVM
    {
        public int CriteriaId { get; set; }
        public string Name { get; set; } = "";
    }

    public class CriteriaGroupVM
    {
        public string GroupName { get; set; } = "Common Criteria";
        public List<CriteriaInputVM> Items { get; set; } = new();
    }

    public class SelectedSkillInputVM
    {
        public int SkillsId { get; set; }
        public string Name { get; set; } = "";
        public string? CategoryName { get; set; }
    }

    public class CriteriaScoreInput
    {
        public int CriteriaId { get; set; }
        public int Score { get; set; }
    }

    public class SkillScoreInput
    {
        public int SkillsId { get; set; }
        public int Score { get; set; }
    }

    // ── Per-skill & per-category rating breakdowns (RN SkillRatingsGroup) ─────
    public class SkillRatingRowVM
    {
        public int SkillsId { get; set; }
        public string Name { get; set; } = "";
        public double? AverageRating { get; set; }
        public int RatingCount { get; set; }
    }

    public class PartTimeSkillGroupVM
    {
        public int CategoryId { get; set; }
        public string CategoryName { get; set; } = "Additional Service";
        public List<string> Skills { get; set; } = new();
        public List<SkillRatingRowVM> SkillRatings { get; set; } = new();
        public double? AverageRating { get; set; }
        public int RatingCount { get; set; }
    }

    // ── Offered skill groups when booking an interview (RN SelectInterviewSkills)
    public class OfferedSkillItemVM
    {
        public int SkillsId { get; set; }
        public string SkillName { get; set; } = "";
    }

    public class OfferedSkillCategoryGroupVM
    {
        public int CategoryId { get; set; }
        public string CategoryName { get; set; } = "";
        public List<OfferedSkillItemVM> Skills { get; set; } = new();
    }

    // ── Worker Certificates (RN WorkerCertificationDetail + RecordDetail) ─────
    public class WorkerCertificateRecordVM
    {
        public int CertificateId { get; set; }
        public int WorkerId { get; set; }
        public string WorkerName { get; set; } = "";
        public string? WorkerPicture { get; set; }
        public string CertificateType { get; set; } = "CompanyCertification"; // CompanyCertification | CourseCompletion
        public string CertificateTitle { get; set; } = "";
        public string CompanyName { get; set; } = "Training Provider";
        public string? EvaluationNotes { get; set; }
        public string? CourseDescription { get; set; }
        public string? Syllabus { get; set; }
        public List<string> SyllabusItems { get; set; } = new();
        public string IssuedDate { get; set; } = "";
        public string? CourseStartDate { get; set; }
        public string? CourseEndDate { get; set; }
        public int? DurationDays { get; set; }
        public string? CompletionDate { get; set; }
        public string? CertificateCode { get; set; }
        public string? CertificateUrl { get; set; }
    }

    public class WorkerCertificateListVM
    {
        public int WorkerId { get; set; }
        public string WorkerName { get; set; } = "Worker";
        public string? WorkerPicture { get; set; }
        public string ViewerRole { get; set; } = "Client"; // Client | Worker
        public List<WorkerCertificateRecordVM> Certificates { get; set; } = new();
    }

    // ── Contract Exit Summary (RN WorkerTerminatedScreen) ─────────────────────
    public class ContractExitSummaryVM
    {
        public int InterviewId { get; set; }
        public int WorkerId { get; set; }
        public string Status { get; set; } = "Terminated"; // Terminated | Resigned
        public string Date { get; set; } = "N/A";
        public string Reason { get; set; } = "No details specified";
        public string WorkerName { get; set; } = "Worker";
        public string? WorkerPicture { get; set; }
        public string? WorkerPhone { get; set; }
        public string? WorkerAddress { get; set; }
        public string WorkerSkill { get; set; } = "Worker";
        public string ClientName { get; set; } = "Client";
        public string? ClientPicture { get; set; }
        public string? ClientAddress { get; set; }
        public string ViewerRole { get; set; } = "Client";
    }

    // ── Standalone Ratings & Reviews page (RN RatingAndReviewsScreen) ─────────
    public class RatingsAndReviewsPageVM
    {
        public int WorkerId { get; set; }
        public string WorkerName { get; set; } = "Worker";
        public string? WorkerPicture { get; set; }
        public string WorkerRole { get; set; } = "General Worker";
        public double AverageRating { get; set; }
        public int ReviewCount { get; set; }
        public string ViewerRole { get; set; } = "Client";
        public List<ReviewVM> Reviews { get; set; } = new();
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
        public bool CanViewContact { get; set; }
        public bool CanHire { get; set; }
        public bool IsExclusiveFullTime { get; set; }
        public string ContractStatus { get; set; } = "";

        // ── Primary + Part-time skill ratings (RN SkillRatingsGroup) ──
        public string PrimaryRole { get; set; } = "General Worker";
        public double? PrimaryCategoryAverageRating { get; set; }
        public int PrimaryCategoryRatingCount { get; set; }
        public List<SkillRatingRowVM> PrimarySkillRatings { get; set; } = new();
        public List<PartTimeSkillGroupVM> PartTimeSkills { get; set; } = new();

        // ── Trust & Verification ──
        public string PoliceCertStatus { get; set; } = "NotIssued"; // Verified | Expired | NotIssued
        public string? PoliceCertNo { get; set; }
        public string? PoliceCharacterStatus { get; set; }
        public string? PoliceIssuingStation { get; set; }
        public string? PoliceIssuingBadge { get; set; }
        public string? PoliceIssuedDate { get; set; }
        public string? PoliceExpiryDate { get; set; }
        public string? PoliceRemarks { get; set; }
        public List<WorkerCertItemVM> CompanyCertificates { get; set; } = new();

        public bool HasVerifiedTraining { get; set; }
        public int TotalCertificateCount { get; set; }
        public string? TrainingTitle { get; set; }
        public string? TrainingCompany { get; set; }
        public string? TrainingIssuedDate { get; set; }
        public string? TrainingCode { get; set; }
        public bool TrainingIsCourse { get; set; }
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
        public List<OfferedSkillCategoryGroupVM> OfferedSkillGroups { get; set; } = new();
        public List<int> SelectedSkillIds { get; set; } = new();
        public DateTime? SelectedDate { get; set; }
        public int? SelectedSlotId { get; set; }
        public bool IsResidenceProvided { get; set; }
        public bool IsExclusive { get; set; }
        public bool HasActiveFullTimeJob { get; set; }
        public bool ConflictActiveFullTime { get; set; }
    }

    // ── Requests / hiring handshake ───────────────────────────────────────────
    public class InterviewRequestVM
    {
        public int InterviewId { get; set; }
        public int OtherPartyId { get; set; }
        public string OtherPartyName { get; set; } = "";
        public string? OtherPartyPicture { get; set; }
        public string? OtherPartyPhone { get; set; }
        public double OtherPartyRating { get; set; }
        public int OtherPartyReviewCount { get; set; }
        public string CategoryName { get; set; } = "";
        public string Location { get; set; } = "";
        public string Time { get; set; } = "";
        public string Status { get; set; } = "";
        public string WorkerDecision { get; set; } = "";
        public string JobType { get; set; } = "Full-Time";
        public bool? IsResidenceProvided { get; set; }
        public int? HiringId { get; set; }
        public string HiringWorkerDecision { get; set; } = "";
        public string HiringDecision { get; set; } = "";
        public string? SlotStart { get; set; }
        public string? SlotEnd { get; set; }
        public bool IsExclusive { get; set; }
        public int? ResignationId { get; set; }
        public string? HiringDate { get; set; }
        public List<string> SelectedSkills { get; set; } = new();

        // Rich lifecycle UI helpers (RN ActiveRequestScreen & ActiveRequestsScreen)
        public string TabGroup { get; set; } = "pending"; // pending | approved | other
        public string BadgeLabel { get; set; } = "Pending";
        public string BadgeClass { get; set; } = "chip-amber";
        public string? StatusHint { get; set; }
        public bool CanApprove { get; set; }
        public bool CanDelete { get; set; }
        public bool CanRespond { get; set; } = true;
        public bool IsHighlighted { get; set; }
    }

    // ── Resignations ──────────────────────────────────────────────────────────
    public class ResignationVM
    {
        public int ResignationId { get; set; }
        public int InterviewId { get; set; }
        public int WorkerId { get; set; }
        public string WorkerName { get; set; } = "";
        public string WorkerRole { get; set; } = "Worker";
        public string? WorkerAvatar { get; set; }
        public string Reason { get; set; } = "";
        public string LastWorkingDate { get; set; } = "";
        public string SubmittedDate { get; set; } = "";
        public string Status { get; set; } = "";
        public int TotalNoticeDays { get; set; } = 7;
        public int RemainingDays { get; set; }
        public double Progress { get; set; }
        public string? NoticeEndsAt { get; set; }

        public bool IsConfirmed { get; set; }
        public string? AcceptanceMode { get; set; }
        public string? AcceptedAt { get; set; }
        public bool CanClientReview => IsConfirmed && ClientReview == null;

        public ReviewVM? WorkerReview { get; set; }
        public ReviewVM? ClientReview { get; set; }

        public List<CriteriaGroupVM> CriteriaGroups { get; set; } = new();
        public List<SelectedSkillInputVM> SelectedSkills { get; set; } = new();
    }

    // ── Pending post-resignation Worker->Client review item ───────────────────
    public class PendingClientReviewItemVM
    {
        public int ResignationId { get; set; }
        public int? InterviewId { get; set; }
        public string ClientName { get; set; } = "Client";
        public string? ClientPicture { get; set; }
        public DateTime? AcceptedAtUtc { get; set; }
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

        public string PrimaryRole { get; set; } = "General Worker";
        public List<string> PrimarySkills { get; set; } = new();
        public double? PrimaryCategoryAverageRating { get; set; }
        public int PrimaryCategoryRatingCount { get; set; }
        public List<SkillRatingRowVM> PrimarySkillRatings { get; set; } = new();
        public List<PartTimeSkillGroupVM> PartTimeSkills { get; set; } = new();

        public int PendingRequestCount { get; set; }
        public int JobNotificationCount { get; set; }
        public int TerminationCount { get; set; }

        // Pending post-resignation client reviews (RN Rate your client card)
        public List<PendingClientReviewItemVM> PendingClientReviews { get; set; } = new();

        // Police & training verification status on Worker dashboard
        public string PoliceCertStatus { get; set; } = "NotIssued";
        public string? PoliceCertNo { get; set; }
        public string? PoliceExpiryDate { get; set; }
        public int TotalCertificateCount { get; set; }
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
        public bool IsExclusive { get; set; }
        public bool IsResignationPending { get; set; }
        public string? NoticeEndsAt { get; set; }
    }

    // ── PUBLIC CLIENT PROFILE ─────────────────────────────────────────────────
    public class ClientProfileVM
    {
        public int ClientId { get; set; }
        public string Name { get; set; } = "";
        public string? Picture { get; set; }
        public string? Address { get; set; }
        public double Rating { get; set; }
        public int ReviewCount { get; set; }
        public List<ClientProfileReviewVM> Reviews { get; set; } = new();
    }

    public class ClientProfileReviewVM
    {
        public string ReviewerName { get; set; } = "Anonymous Worker";
        public string? ReviewerPicture { get; set; }
        public decimal Rating { get; set; }
        public string? Comment { get; set; }
        public string Date { get; set; } = "";
        public string? WorkedPeriod { get; set; }
        public string? EndReasonType { get; set; }
        public string? EndReason { get; set; }
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
        public string Status { get; set; } = "NotIssued"; // Verified | Expired | NotIssued
        public string? CertificateNo { get; set; }
        public string? CharacterStatus { get; set; }
        public string? Remarks { get; set; }
        public string? IssuingStation { get; set; }
        public string? IssuingBadge { get; set; }
        public string? IssuedDate { get; set; }
        public string? ExpiryDate { get; set; }
        public int? DaysUntilExpiry { get; set; }
        public string ViewerRole { get; set; } = "Client"; // Client | Worker | Police
    }
}
