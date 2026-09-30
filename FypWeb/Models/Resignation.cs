using System;
using System.Collections.Generic;

namespace Fyp_Backend.Models;

public partial class Resignation
{
    public int ResignationId { get; set; }

    public int? InterviewId { get; set; }

    public string ResignationReason { get; set; } = null!;

    public DateOnly LastWorkingDate { get; set; }

    public DateTime? SubmittedDate { get; set; }

    public virtual Interview? Interview { get; set; }
}
