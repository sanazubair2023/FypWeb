using System;
using System.Collections.Generic;

namespace Fyp_Backend.Models;

public partial class Review
{
    public int ReviewId { get; set; }

    public int? InterviewId { get; set; }

    /// <summary>
    /// Overall score, 1.00-5.00.
    /// For Client -> Worker reviews this is the AVERAGE of the per-criterion
    /// scores in ReviewCriteriaRatings (hence decimal, not int).
    /// Worker -> Client reviews still write a whole number here.
    /// </summary>
    public decimal? Rating { get; set; }

    public string? Comment { get; set; }

    public DateTime? ReviewDate { get; set; }

    public string? ReviewerRole { get; set; }

    public virtual Interview? Interview { get; set; }

    public virtual ICollection<ReviewCriteriaRating> ReviewCriteriaRatings { get; set; } = new List<ReviewCriteriaRating>();
}
