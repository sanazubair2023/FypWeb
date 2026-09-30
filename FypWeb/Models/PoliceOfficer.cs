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
        public string StationName { get; set; }

        [Required]
        [StringLength(50)]
        public string BadgeID { get; set; }

        [Required]
        [StringLength(100)]
        public string Email { get; set; }

        [Required]
        [StringLength(255)]
        public string Password { get; set; }

        [Required]
        [StringLength(20)]
        public string PhoneNo { get; set; }

        [Required]
        [StringLength(255)]
        public string JurisdictionAddress { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }
}