using System;
using System.ComponentModel.DataAnnotations;

namespace Fyp_Backend.Models
{
    public class PoliceOfficer
    {
        [Key]
        public int PoliceID { get; set; }

        [Required]
        [StringLength(150)]
        public string StationName { get; set; } = string.Empty;

        [Required]
        [StringLength(50)]
        public string BadgeID { get; set; } = string.Empty;

        [Required]
        [StringLength(100)]
        public string Email { get; set; } = string.Empty;

        [Required]
        [StringLength(255)]
        public string Password { get; set; } = string.Empty;

        [Required]
        [StringLength(20)]
        public string PhoneNo { get; set; } = string.Empty;

        [Required]
        [StringLength(255)]
        public string JurisdictionAddress { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }
}