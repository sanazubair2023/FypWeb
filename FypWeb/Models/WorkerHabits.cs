using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Fyp_Backend.Models
{
    // Which habits a worker selected. One row per worker+habit (unique index),
    // so a worker can hold as many habits as they like and the client filter can
    // ask "does this worker have habit N?" with a single EXISTS.
    [Table("WorkerHabits")]
    public partial class WorkerHabits
    {
        [Key]
        [Column("WorkerHabitId")]
        public int WorkerHabitId { get; set; }

        [Column("WorkerId")]
        public int WorkerId { get; set; }

        [Column("HabitId")]
        public int HabitId { get; set; }

        [Column("CreatedDate")]
        public DateTime? CreatedDate { get; set; }

        [ForeignKey("WorkerId")]
        public virtual Worker? Worker { get; set; }

        [ForeignKey("HabitId")]
        public virtual Habit? Habit { get; set; }
    }
}
