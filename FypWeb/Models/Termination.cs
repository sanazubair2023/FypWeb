using System;
using System.Collections.Generic;

namespace Fyp_Backend.Models;

public partial class Termination
{
    public int TerminationId { get; set; }

    public int? InterviewId { get; set; }

    public DateOnly? TerminatedDate { get; set; }

    public string? TerminatedReason { get; set; }

    public virtual Interview? Interview { get; set; }
}
