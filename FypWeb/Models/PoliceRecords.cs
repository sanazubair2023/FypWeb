using System;

namespace Fyp_Backend.Models
{
    // ─────────────────────────────────────────────────────────────────────────
    // REPURPOSED TABLE (was the old "criminal / FIR record" table).
    //
    // It now stores a POSITIVE, police-issued **Character Certificate** for a
    // worker. The certificate is valid for 5 years (ExpiryDate = IssuedDate + 5y)
    // and there is at most ONE active (non-revoked) certificate per worker — a
    // re-issue renews the same row.
    //
    // The old offense columns (FIRNumber, OffenseCategory, OffenseDate,
    // IsFlagged, IsBlocked, CaseDetails) were dropped by the ALTER script
    // Repurpose_PoliceRecords_To_CharacterCertificate.sql.
    // ─────────────────────────────────────────────────────────────────────────
    public class PoliceRecords
    {
        public int RecordID { get; set; }

        // Who the certificate is about.
        public int WorkerID { get; set; }

        // Which officer issued it (from the JWT of the logged-in police account).
        public int? PoliceID { get; set; }

        // Auto-generated reference number, e.g. "CC-12-20260927".
        public string? CertificateNo { get; set; }

        // Character verdict, e.g. "Clear" / "Good Character".
        public string? CharacterStatus { get; set; }

        // Free-text notes the officer types in ("detail").
        public string? Remarks { get; set; }

        // The CNIC the officer confirmed at the counter (snapshot) + a flag.
        public string? VerifiedCnic { get; set; }
        public bool CnicVerified { get; set; }

        // Issuing authority snapshot, auto-filled from the police account.
        public string? IssuingStation { get; set; }
        public string? IssuingBadge { get; set; }

        // 5-year validity window.
        public DateTime IssuedDate { get; set; } = DateTime.Now;
        public DateTime ExpiryDate { get; set; }

        // Soft-revoke without deleting history.
        public bool IsRevoked { get; set; } = false;

        // Navigation
        public virtual PoliceOfficer? PoliceOfficer { get; set; }
        public virtual Worker? Worker { get; set; }
    }
}
