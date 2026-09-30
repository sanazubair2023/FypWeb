using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;

namespace Fyp_Backend.Models;

public partial class Worker
{
    public int WorkerId { get; set; }

    public string? Name { get; set; }
    public string? Cnic { get; set; }
    public string? Phone { get; set; }
    public decimal? Salary { get; set; }
    public string? Address { get; set; }
    public string? Picture { get; set; } = "worker_default.jpg";
    [NotMapped]
    public IFormFile? PictureFile { get; set; }
    public bool? AvailableStatus { get; set; }
    public int? Age { get; set; }
    public string? Password { get; set; }
    public string? Gender { get; set; }
    public string? Bio { get; set; }
    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }
    public int Radius { get; set; } = 5;

    public virtual ICollection<Experience> Experiences { get; set; } = new List<Experience>();

    public virtual ICollection<WorkerHabits> WorkerHabits { get; set; } = new List<WorkerHabits>();

    public virtual ICollection<Interview> Interviews { get; set; } = new List<Interview>();
}
