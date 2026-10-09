using System;
using System.Collections.Generic;

namespace Fyp_Backend.Models;

/// <summary>
/// A single review question. CategoryId == null means the criterion applies to
/// every profession; a set CategoryId scopes it to that profession only.
/// </summary>
public partial class ReviewCriteria
{
    public int CriteriaId { get; set; }

    public int? CategoryId { get; set; }

    public string CriteriaName { get; set; } = null!;

    public int SortOrder { get; set; }

    public bool IsActive { get; set; } = true;

    public virtual Category? Category { get; set; }

    public virtual ICollection<ReviewCriteriaRating> ReviewCriteriaRatings { get; set; } = new List<ReviewCriteriaRating>();
}
