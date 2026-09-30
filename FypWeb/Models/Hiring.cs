using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Fyp_Backend.Models
{
    [Table("Hiring")]
    public partial class Hiring
    {
        [Key]
        [Column("Hiring_id")]
        public int HiringId { get; set; }

        [Column("interview_id")]
        public int? InterviewId { get; set; }

        [Column("WorkerDecision")]
        [StringLength(50)]
        public string? WorkerDecision { get; set; }

        [Column("Hiring_Decision")]
        [StringLength(50)]
        public string? HiringDecision { get; set; }

        [Column("Address")]
        public string? Address { get; set; }

        [Column("Hiring_Date")]
        public DateTime? HiringDate { get; set; }

        [ForeignKey("InterviewId")]
        public virtual Interview? Interview { get; set; }
    }
}
