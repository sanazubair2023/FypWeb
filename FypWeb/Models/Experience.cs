using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;

namespace Fyp_Backend.Models;

public partial class Experience
{
    public int ExperienceId { get; set; }

    public int? WorkerId { get; set; }

    public string? WorkAt { get; set; }

    public string? ExpDetail { get; set; }

    public string? Duration { get; set; }

    [NotMapped]
    public int? CategoryId { get; set; }

    [NotMapped]
    public int? SkillsId { get; set; }

    public virtual Worker? Worker { get; set; }
}
