using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Fyp_Backend.Models
{
    [Table("WorkerTimeSlots")]
    public class WorkerTimeSlots
    {
        [Key]
        public int Id { get; set; }
        
        [Column("WorkerId")]
        public int WorkerId { get; set; }
        
        [Column("StartTime")]
        public TimeSpan StartTime { get; set; }
        
        [Column("EndTime")]
        public TimeSpan EndTime { get; set; }
    }
}
