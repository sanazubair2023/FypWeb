using System;
using System.Collections.Generic;

namespace Fyp_Backend.Models;

public partial class Skill
{
    public int SkillsId { get; set; }

    public int? CategoryId { get; set; }

    public string? SkillName { get; set; }

    public virtual Category? Category { get; set; }
}
