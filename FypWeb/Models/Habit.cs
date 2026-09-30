using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Fyp_Backend.Models
{
    // Master list of habits. Every row here is offered as a checkbox during
    // worker signup / edit and as a filter option on the client side, so the
    // wording stays identical everywhere (and old rows never change under a
    // worker who already selected them).
    [Table("Habits")]
    public partial class Habit
    {
        [Key]
        [Column("HabitId")]
        public int HabitId { get; set; }

        [Required]
        [StringLength(60)]
        [Column("Name")]
        public string Name { get; set; } = null!;

        // Controls the display order of the checkbox list.
        [Column("SortOrder")]
        public int SortOrder { get; set; }

        // Retire a habit without deleting it (workers keep the history).
        [Column("IsActive")]
        public bool IsActive { get; set; } = true;

        public virtual ICollection<WorkerHabits> WorkerHabits { get; set; } = new List<WorkerHabits>();
    }
}
