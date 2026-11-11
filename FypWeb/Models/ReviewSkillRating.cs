namespace Fyp_Backend.Models;

/// <summary>
/// One client score (1-5) for one skill selected on the reviewed interview.
/// This score is stored separately from Reviews.Rating and never contributes
/// to the common-criteria overall rating.
/// </summary>
public partial class ReviewSkillRating
{
    public int ReviewId { get; set; }

    public int SkillsId { get; set; }

    public int Score { get; set; }

    public virtual Review Review { get; set; } = null!;

    public virtual Skill Skill { get; set; } = null!;
}
