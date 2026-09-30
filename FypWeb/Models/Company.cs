using System;
using System.ComponentModel.DataAnnotations;

namespace Fyp_Backend.Models
{
    public class Company
    {
        [Key]
        public int CompanyID { get; set; }

        [Required]
        [StringLength(150)]
        public string CompanyName { get; set; }

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
        [StringLength(50)]
        public string LicenseNumber { get; set; }

        [Required]
        [StringLength(255)]
        public string CompanyAddress { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public string? CompanyPicture { get; set; } = "company_default.jpg";
    }
}