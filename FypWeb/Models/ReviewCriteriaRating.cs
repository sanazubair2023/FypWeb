using System;
using System.Collections.Generic;

namespace Fyp_Backend.Models;

/// <summary>
/// One score (1-5) for one criterion within one review.
/// The profession a review was about is implicit in the criteria rows written
/// here, so Reviews needs no Category_ID column of its own.
/// </summary>
public partial class ReviewCriteriaRating
{
    public int CriteriaRatingId { get; set; }

    public int ReviewId { get; set; }

    public int CriteriaId { get; set; }

    public int Score { get; set; }

    public virtual Review Review { get; set; } = null!;

    public virtual ReviewCriteria Criteria { get; set; } = null!;
}
