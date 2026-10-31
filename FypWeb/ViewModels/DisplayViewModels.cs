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

        // RN "Certified" chip: legacy WorkerCertifications ∪ non-revoked
        // CourseCertificates (backend GetWorkers isCompanyCertified).
        public bool IsCompanyCertified { get; set; }
    }

    public class ReviewVM
    {
        // Overall is the average of the per-criterion scores (e.g. 4.33) for
        // client -> worker reviews; whole numbers for worker -> client reviews.
        public decimal Rating { get; set; }
        public string? Comment { get; set; }
        public string ReviewerRole { get; set; } = "Client";
        public string ReviewerName { get; set; } = "Anonymous";
        public string ReviewDate { get; set; } = "";
        // "dd MMM – dd MMM yyyy (N days)" — contract window behind the review
        // (RN review card: "Worked: …"). Null when no contract dates exist.
        public string? WorkedPeriod { get; set; }

        // How the contract ended ("Terminated" / "Resigned" / …) — drives the
        // red/violet badge on the RN ReviewCard. Null/blank = no badge.
        public string? EndType { get; set; }

        // Per-criterion breakdown [{ Name, Score }] — empty for legacy reviews.
        public List<CriteriaScoreVM> Criteria { get; set; } = new();

        // Who wrote the review (client id) — RN navigates to ClientProfileScreen
        // with rev.clientId when the reviewer name is pressed (WorkerDetail only).
        public int? ReviewerClientId { get; set; }
        public int? ReviewerId { get; set; }
    }

    // One criterion row displayed on a review card (RN CriteriaBreakdown).
    public class CriteriaScoreVM
    {
        public string Name { get; set; } = "";
        public int Score { get; set; }
    }

    // ── DETAIL-REVIEW INPUT (RN CriteriaReviewSection) ────────────────────────
    // Star rows rendered when a client closes a contract: the common criteria
    // ("Basics") plus one group per profession the worker holds.
    public class CriteriaInputVM
    {
        public int CriteriaId { get; set; }
        public string Name { get; set; } = "";
    }

    public class CriteriaGroupVM
    {
        public string GroupName { get; set; } = "Basics";
        public List<CriteriaInputVM> Items { get; set; } = new();
    }

    // Form-posted score for one criterion (binds criteriaScores[i].CriteriaId / .Score).
    public class CriteriaScoreInput
    {
        public int CriteriaId { get; set; }
        public int Score { get; set; }
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

        // Verified Training card (RN WorkerDetailScreen / GetWorkerCertificateDetail):
        // the legacy company certification, or else the newest non-revoked
        // course-completion certificate. One card — null title = none.
        public bool HasVerifiedTraining { get; set; }
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
        public string? SlotStart { get; set; }
        public string? SlotEnd { get; set; }
        public bool IsExclusive { get; set; }
        public int? ResignationId { get; set; }
        public string? HiringDate { get; set; }
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

        // Detail view (RN ResignationScreen): has the client already confirmed?
        public bool IsConfirmed { get; set; }
        public string? AcceptanceMode { get; set; }
        public string? AcceptedAt { get; set; }
        public bool CanClientReview => IsConfirmed && ClientReview == null;

        // The worker's own closing review (written when resigning).
        public ReviewVM? WorkerReview { get; set; }

        // The client's criteria-scored review, shown locked after confirming.
        public ReviewVM? ClientReview { get; set; }

        // Star rows for the client's criteria input (empty once confirmed).
        public List<CriteriaGroupVM> CriteriaGroups { get; set; } = new();
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

        // Skills tab (RN WorkerDashboardScreen): primary category + its skills,
        // and every other category grouped as secondary/part-time services.
        public string PrimaryRole { get; set; } = "General Worker";
        public List<string> PrimarySkills { get; set; } = new();
        public List<PartTimeSkillGroupVM> PartTimeSkills { get; set; } = new();

        // Overview notification counters (RN WorkerDashboardScreen header cards —
        // api/Dashboard/GetWorkerDetail: pendingRequestCount / jobNotificationCount /
        // terminationCount).
        public int PendingRequestCount { get; set; }
        public int JobNotificationCount { get; set; }
        public int TerminationCount { get; set; }
    }

    public class PartTimeSkillGroupVM
    {
        public string CategoryName { get; set; } = "Additional Service";
        public List<string> Skills { get; set; } = new();
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
    }

    // ── PUBLIC CLIENT PROFILE (RN ClientProfileScreen / api GetClientDetail) ──
    // Read-only profile of a client, viewable by clients (from worker-detail
    // review names) and workers (from request cards). Contact details are
    // deliberately NOT included — the RN screen never shows them.
    public class ClientProfileVM
    {
        public int ClientId { get; set; }
        public string Name { get; set; } = "";
        public string? Picture { get; set; }
        public string? Address { get; set; }
        public double Rating { get; set; }          // 0 => "New" badge
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
