using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Fyp_Backend.Models
{
    [Table("Hiring")]
    public class Hiring
    {
        [Key]
        [Column("Hiring_id")]
        public int HiringId { get; set; }

        [Column("interview_id")]
        public int? InterviewId { get; set; }

        [ForeignKey("InterviewId")]
        public Interview? Interview { get; set; }

        [Column("WorkerDecision")]
        public string? WorkerDecision { get; set; }

        [Column("Hiring_Decision")]
        public string? HiringDecision { get; set; }

        [Column("Address")]
        public string? Address { get; set; }

        [Column("HiringDate")] // <-- Yeh lazmi hona chahiye
        public DateTime? HiringDate { get; set; }
    }
}