using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Fyp_Backend.Models
{
    public class WorkerCertification
    {
        [Key]
        public int CertificationID { get; set; }

        [Required]
        public int WorkerID { get; set; }

        [Required]
        public int CompanyID { get; set; }

        [Required]
        [StringLength(150)]
        public string CertificateTitle { get; set; }

        public string TrainingEvaluationNotes { get; set; }

        public DateTime IssuedDate { get; set; } = DateTime.Now;

        [ForeignKey("CompanyID")]
        public virtual Company Company { get; set; }

        [ForeignKey("WorkerID")]
        public virtual Worker Worker { get; set; }
    }
}