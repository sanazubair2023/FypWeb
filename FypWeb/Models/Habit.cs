using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Fyp_Backend.Models
{
    // Master list of selectable worker attributes. Audience flags keep the
    // worker profile checklist separate from the client's search filters.
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

        // Controls display order in worker and client lists.
        [Column("SortOrder")]
        public int SortOrder { get; set; }

        // Retire a habit without deleting it (workers keep the history).
        [Column("IsActive")]
        public bool IsActive { get; set; } = true;

        // A worker may select this value in signup / profile editing.
        [Column("IsWorkerSelectable")]
        public bool IsWorkerSelectable { get; set; } = true;

        // A client may use this value as a search filter.
        [Column("IsClientFilterable")]
        public bool IsClientFilterable { get; set; }

        // Values with the same group are mutually exclusive (for example,
        // Smoker / Non-Smoker). Null means the habit is independent.
        [StringLength(40)]
        [Column("ExclusiveGroup")]
        public string? ExclusiveGroup { get; set; }

        public virtual ICollection<WorkerHabits> WorkerHabits { get; set; } = new List<WorkerHabits>();
    }
}
