using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Fyp_Backend.Models;

public partial class Fyp1Context : DbContext
{
    public Fyp1Context()
    {
    }

    public Fyp1Context(DbContextOptions<Fyp1Context> options)
        : base(options)
    {
    }

    public virtual DbSet<Category> Categories { get; set; }
    public virtual DbSet<Client> Clients { get; set; }
    public virtual DbSet<Experience> Experiences { get; set; }
    public virtual DbSet<Interview> Interviews { get; set; }
    public virtual DbSet<Resignation> Resignations { get; set; }
    public virtual DbSet<Review> Reviews { get; set; }
    public virtual DbSet<ReviewCriteria> ReviewCriteria { get; set; }
    public virtual DbSet<ReviewCriteriaRating> ReviewCriteriaRatings { get; set; }
    public virtual DbSet<Skill> Skills { get; set; }
    public virtual DbSet<Termination> Terminations { get; set; }
    public virtual DbSet<Worker> Workers { get; set; }
    public virtual DbSet<Hiring> Hiring { get; set; }

    // NEW DBSETS
    public virtual DbSet<Company> Companies { get; set; }
    public virtual DbSet<PoliceOfficer> PoliceOfficers { get; set; }
    public virtual DbSet<WorkerCertification> WorkerCertifications { get; set; }
    public virtual DbSet<PoliceRecords> PoliceRecords { get; set; }
    public virtual DbSet<WorkerTimeSlots> WorkerTimeSlots { get; set; }

    // Updated naming to match standard conventions
    public virtual DbSet<WorkerCategory> WorkerCategories { get; set; }

    public virtual DbSet<Habit> Habits { get; set; }

    public virtual DbSet<WorkerHabits> WorkerHabits { get; set; }
    public virtual DbSet<Course> Courses { get; set; }
    public virtual DbSet<CourseEnrollment> CourseEnrollments { get; set; }
    public virtual DbSet<CourseCertificate> CourseCertificates { get; set; }
    public virtual DbSet<WorkerNotification> WorkerNotifications { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Category>(entity =>
        {
            entity.HasKey(e => e.CategoryId).HasName("PK__Category__6DB38D4E65D868F5");
            entity.ToTable("Category");
            entity.Property(e => e.CategoryId).HasColumnName("Category_ID");
            entity.Property(e => e.CategoryName)
                .HasMaxLength(50)
                .IsUnicode(false)
                .HasColumnName("Category_Name");
        });

        // Junction Table Configuration (Many-to-Many)
        modelBuilder.Entity<WorkerCategory>(entity =>
        {
            entity.ToTable("Worker_Category");

            // Define Composite Primary Key (Worker + Category + Skill)
            entity.HasKey(e => new { e.WorkerId, e.CategoryId, e.SkillsId });

            entity.Property(e => e.WorkerId).HasColumnName("Worker_ID");
            entity.Property(e => e.CategoryId).HasColumnName("Category_ID");
            entity.Property(e => e.SkillsId).HasColumnName("Skills_ID");
            entity.Property(e => e.IsPrimary).HasDefaultValue(false);

            entity.HasOne<Worker>()
                .WithMany()
                .HasForeignKey(e => e.WorkerId);
        });

        modelBuilder.Entity<Client>(entity =>
        {
            entity.HasKey(e => e.ClientId).HasName("PK__Client__75A5D7185A7AEB78");
            entity.ToTable("Client");
            entity.HasIndex(e => e.Email, "UQ__Client__A9D105344DD91BD0").IsUnique();
            entity.Property(e => e.ClientId).HasColumnName("Client_ID");
            entity.Property(e => e.Address).HasColumnType("text");
            entity.Property(e => e.Email).HasMaxLength(100).IsUnicode(false);
            entity.Property(e => e.Name).HasMaxLength(100).IsUnicode(false);
            entity.Property(e => e.Password).HasMaxLength(255).IsUnicode(false);
            entity.Property(e => e.Phone).HasMaxLength(20).IsUnicode(false);
            entity.Property(e => e.Picture).HasMaxLength(255).IsUnicode(false);
            entity.Property(e => e.Latitude).HasColumnName("Latitude");
            entity.Property(e => e.Longitude).HasColumnName("Longitude");
        });

        modelBuilder.Entity<Experience>(entity =>
        {
            entity.HasKey(e => e.ExperienceId).HasName("PK__Experien__177FAF2EB1CE0EEA");
            entity.ToTable("Experience");
            entity.Property(e => e.ExperienceId).HasColumnName("Experience_ID");
            entity.Property(e => e.Duration).HasMaxLength(50).IsUnicode(false);
            entity.Property(e => e.ExpDetail).HasColumnType("text").HasColumnName("Exp_Detail");
            entity.Property(e => e.WorkAt).HasMaxLength(150).IsUnicode(false).HasColumnName("Work_At");
            entity.Property(e => e.WorkerId).HasColumnName("Worker_ID");

            entity.HasOne(d => d.Worker).WithMany(p => p.Experiences)
                .HasForeignKey(d => d.WorkerId)
                .HasConstraintName("FK__Experienc__Worke__571DF1D5");
        });

        modelBuilder.Entity<Interview>(entity =>
        {
            entity.HasKey(e => e.InterviewId).HasName("PK__Intervie__536D7219E4B71E80");
            entity.ToTable("Interview");
            entity.Property(e => e.InterviewId).HasColumnName("Interview_ID");
            entity.Property(e => e.Address).HasColumnType("text");
            entity.Property(e => e.ClientId).HasColumnName("Client_ID");
            entity.Property(e => e.InterviewDate).HasColumnType("datetime").HasColumnName("Interview_Date");
            entity.Property(e => e.Status).HasMaxLength(50).IsUnicode(false);
            entity.Property(e => e.WorkerId).HasColumnName("Worker_ID");
            entity.Property(e => e.JobType).HasMaxLength(20).IsUnicode(false);
            entity.Property(e => e.SlotId).HasColumnName("SlotId");
            entity.Property(e => e.IsResidenceProvided).HasColumnName("IsResidenceProvided");
            entity.Property(e => e.IsExclusive).HasColumnName("IsExclusive").HasDefaultValue(false);

            // Optional link to the picked time slot. Schedule slots are hidden via
            // IsActive while a Part-Time hire is active rather than deleted.
            entity.HasOne(d => d.Slot)
                .WithMany()
                .HasForeignKey(d => d.SlotId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(d => d.Client).WithMany(p => p.Interviews)
                .HasForeignKey(d => d.ClientId)
                .HasConstraintName("FK__Interview__Clien__59FA5E80");

            entity.HasOne(d => d.Worker).WithMany(p => p.Interviews)
                .HasForeignKey(d => d.WorkerId)
                .HasConstraintName("FK__Interview__Worke__5AEE82B9");
        });

        modelBuilder.Entity<Review>(entity =>
        {
            entity.HasKey(e => e.ReviewId).HasName("PK__Reviews__F85DA7EB6071CC75");
            entity.HasIndex(e => new { e.InterviewId, e.ReviewerRole }).IsUnique().HasFilter("[Interview_ID] IS NOT NULL");
            entity.Property(e => e.ReviewId).HasColumnName("Review_ID");
            entity.Property(e => e.Comment).HasColumnType("text");
            entity.Property(e => e.InterviewId).HasColumnName("Interview_ID");
            // Overall is now the average of the per-criterion scores, so it holds
            // fractional values (e.g. 4.33) rather than a hand-picked 1-5 int.
            entity.Property(e => e.Rating).HasColumnType("decimal(3,2)");
            entity.Property(e => e.ReviewDate).HasDefaultValueSql("(getdate())").HasColumnType("datetime");
            entity.Property(e => e.ReviewerRole)
                .HasMaxLength(50)
                .IsUnicode(false)
                .HasDefaultValue("Client");

            entity.HasOne(d => d.Interview).WithMany(p => p.Reviews)
                .HasForeignKey(d => d.InterviewId)
                .HasConstraintName("FK__Reviews__Intervi__5FB337D6");
        });

        modelBuilder.Entity<ReviewCriteria>(entity =>
        {
            entity.ToTable("ReviewCriteria");
            entity.HasKey(e => e.CriteriaId);
            entity.Property(e => e.CriteriaId).HasColumnName("Criteria_ID");
            entity.Property(e => e.CategoryId).HasColumnName("Category_ID");
            entity.Property(e => e.CriteriaName).HasColumnName("Criteria_Name").HasMaxLength(100).IsUnicode(false);
            entity.Property(e => e.SortOrder).HasColumnName("Sort_Order").HasDefaultValue(0);
            entity.Property(e => e.IsActive).HasColumnName("Is_Active").HasDefaultValue(true);

            entity.HasIndex(e => new { e.CategoryId, e.CriteriaName }).IsUnique().HasFilter("[Category_ID] IS NOT NULL");

            // CategoryId == null => applies to every profession.
            entity.HasOne(d => d.Category).WithMany()
                .HasForeignKey(d => d.CategoryId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ReviewCriteriaRating>(entity =>
        {
            entity.ToTable("ReviewCriteriaRating");
            entity.HasKey(e => e.CriteriaRatingId);
            entity.Property(e => e.CriteriaRatingId).HasColumnName("Criteria_Rating_ID");
            entity.Property(e => e.ReviewId).HasColumnName("Review_ID");
            entity.Property(e => e.CriteriaId).HasColumnName("Criteria_ID");

            entity.HasIndex(e => new { e.ReviewId, e.CriteriaId }).IsUnique();

            entity.HasOne(d => d.Review).WithMany(p => p.ReviewCriteriaRatings)
                .HasForeignKey(d => d.ReviewId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(d => d.Criteria).WithMany(p => p.ReviewCriteriaRatings)
                .HasForeignKey(d => d.CriteriaId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Resignation>(entity =>
        {
            entity.ToTable("Resignation");
            entity.HasKey(e => e.ResignationId);
            entity.Property(e => e.ResignationId).HasColumnName("Resignation_ID");
            entity.Property(e => e.InterviewId).HasColumnName("Interview_ID");
            entity.Property(e => e.ResignationReason).HasColumnName("Resignation_Reason").HasColumnType("text");
            entity.Property(e => e.LastWorkingDate).HasColumnName("Last_Working_Date").HasColumnType("date");
            entity.Property(e => e.SubmittedDate).HasColumnName("Submitted_Date").HasColumnType("datetime");

            entity.HasOne(d => d.Interview).WithMany(p => p.Resignations)
                .HasForeignKey(d => d.InterviewId);
        });

        modelBuilder.Entity<Termination>(entity =>
        {
            entity.ToTable("Termination");
            entity.HasKey(e => e.TerminationId);
            entity.HasIndex(e => e.InterviewId).IsUnique().HasFilter("[Interview_ID] IS NOT NULL");
            entity.Property(e => e.TerminationId).HasColumnName("Termination_ID");
            entity.Property(e => e.InterviewId).HasColumnName("Interview_ID");
            entity.Property(e => e.TerminatedDate).HasColumnName("Terminated_Date");
            entity.Property(e => e.TerminatedReason).HasColumnName("Terminated_Reason").HasColumnType("text");

            entity.HasOne(d => d.Interview).WithMany(p => p.Terminations)
                .HasForeignKey(d => d.InterviewId);
        });

        modelBuilder.Entity<Skill>(entity =>
        {
            entity.HasKey(e => e.SkillsId).HasName("PK__Skills__7569047CFE2BC2EA");
            entity.Property(e => e.SkillsId).HasColumnName("Skills_ID");
            entity.Property(e => e.CategoryId).HasColumnName("Category_ID");
            entity.Property(e => e.SkillName).HasMaxLength(100).IsUnicode(false).HasColumnName("Skill_Name");

            entity.HasOne(d => d.Category).WithMany(p => p.Skills)
                .HasForeignKey(d => d.CategoryId)
                .HasConstraintName("FK__Skills__Category__4F7CD00D");
        });

        modelBuilder.Entity<Worker>(entity =>
        {
            entity.HasKey(e => e.WorkerId).HasName("PK__Worker__F35E9FF469467C94");
            entity.ToTable("Worker");
            entity.HasIndex(e => e.Cnic, "UQ__Worker__A29801FA2512666E").IsUnique();

            entity.Property(e => e.WorkerId).HasColumnName("Worker_ID");
            entity.Property(e => e.Address).HasColumnType("text");
            entity.Property(e => e.AvailableStatus).HasDefaultValue(true).HasColumnName("Available_Status");

            entity.Property(e => e.Cnic).HasMaxLength(20).IsUnicode(false);
            entity.Property(e => e.Gender).HasMaxLength(10);
            entity.Property(e => e.Name).HasMaxLength(100).IsUnicode(false);
            entity.Property(e => e.Password).HasMaxLength(255).IsUnicode(false);
            entity.Property(e => e.Phone).HasMaxLength(20).IsUnicode(false);
            entity.Property(e => e.Picture).HasMaxLength(255).IsUnicode(false);
            entity.Property(e => e.Salary).HasColumnType("decimal(10, 2)");
            entity.Property(e => e.Latitude).HasColumnName("Latitude").HasColumnType("decimal(9, 6)");
            entity.Property(e => e.Longitude).HasColumnName("Longitude").HasColumnType("decimal(9, 6)");
            entity.Property(e => e.Radius).HasColumnName("Radius").HasDefaultValue(5);
        });

        modelBuilder.Entity<Hiring>(entity =>
        {
            entity.ToTable("Hiring");

            entity.HasKey(e => e.HiringId);
            entity.HasIndex(e => e.InterviewId).IsUnique().HasFilter("[interview_id] IS NOT NULL");

            entity.Property(e => e.HiringId)
                .HasColumnName("Hiring_id");

            entity.Property(e => e.InterviewId)
                .HasColumnName("interview_id");

            entity.Property(e => e.WorkerDecision)
                .HasColumnName("WorkerDecision")
                .HasMaxLength(50)
                .IsUnicode(false);

            entity.Property(e => e.HiringDecision)
                .HasColumnName("Hiring_Decision")
                .HasMaxLength(50)
                .IsUnicode(false);

            entity.Property(e => e.Address)
                .HasColumnName("Address")
                .HasMaxLength(255)
                .IsUnicode(false);

            entity.Property(e => e.HiringDate)
                .HasColumnName("HiringDate");

            entity.HasOne(d => d.Interview)
                .WithMany(p => p.Hirings)
                .HasForeignKey(d => d.InterviewId)
                .HasConstraintName("FK__Hiring__intervie__xxxxxx");
        });

        modelBuilder.Entity<Company>(entity =>
        {
            entity.ToTable("Companies");
            entity.HasKey(e => e.CompanyID);

            entity.HasIndex(e => e.Email).IsUnique();
            entity.HasIndex(e => e.LicenseNumber).IsUnique();
            entity.Property(e => e.CompanyName).HasMaxLength(150);
            entity.Property(e => e.Email).HasMaxLength(100);
            entity.Property(e => e.Password).HasMaxLength(255);
            entity.Property(e => e.PhoneNo).HasMaxLength(20);
            entity.Property(e => e.LicenseNumber).HasMaxLength(50);
            entity.Property(e => e.CompanyAddress).HasMaxLength(255);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(getdate())");
            entity.Property(e => e.CompanyPicture)
                .HasMaxLength(255)
                .HasDefaultValue("company_default.jpg");
        });

        modelBuilder.Entity<PoliceOfficer>(entity =>
        {
            entity.ToTable("PoliceOfficers");
            entity.HasKey(e => e.PoliceID);

            entity.HasIndex(e => e.BadgeID).IsUnique();
            entity.HasIndex(e => e.Email).IsUnique();
            entity.Property(e => e.StationName).HasMaxLength(150);
            entity.Property(e => e.BadgeID).HasMaxLength(50);
            entity.Property(e => e.Email).HasMaxLength(100);
            entity.Property(e => e.Password).HasMaxLength(255);
            entity.Property(e => e.PhoneNo).HasMaxLength(20);
            entity.Property(e => e.JurisdictionAddress).HasMaxLength(255);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(getdate())");
        });

        modelBuilder.Entity<WorkerCertification>(entity =>
        {
            entity.ToTable("WorkerCertifications");
            entity.HasKey(e => e.CertificationID);

            entity.Property(e => e.WorkerID).HasColumnName("WorkerID");
            entity.Property(e => e.CompanyID).HasColumnName("CompanyID");
            entity.Property(e => e.CertificateTitle).HasMaxLength(150);
            entity.Property(e => e.TrainingEvaluationNotes).HasColumnType("nvarchar(max)");
            entity.Property(e => e.IssuedDate).HasDefaultValueSql("(getdate())");

            entity.HasOne(d => d.Company)
                .WithMany()
                .HasForeignKey(d => d.CompanyID);

            entity.HasOne(d => d.Worker)
                .WithMany()
                .HasForeignKey(d => d.WorkerID);
        });

        // Repurposed: now a positive, 5-year Character Certificate (see PoliceRecord.cs)
        modelBuilder.Entity<PoliceRecords>(entity =>
        {
            entity.ToTable("PoliceRecords");
            entity.HasKey(e => e.RecordID);
            entity.HasIndex(e => e.WorkerID).IsUnique();
            entity.HasIndex(e => e.CertificateNo).IsUnique().HasFilter("[CertificateNo] IS NOT NULL");

            entity.Property(e => e.WorkerID).HasColumnName("WorkerID");
            entity.Property(e => e.PoliceID).HasColumnName("PoliceID");
            entity.Property(e => e.CertificateNo).HasMaxLength(50);
            entity.Property(e => e.CharacterStatus).HasMaxLength(50);
            entity.Property(e => e.Remarks).HasColumnType("nvarchar(max)");
            entity.Property(e => e.VerifiedCnic).HasMaxLength(20);
            entity.Property(e => e.CnicVerified).HasDefaultValue(false);
            entity.Property(e => e.IssuingStation).HasMaxLength(150);
            entity.Property(e => e.IssuingBadge).HasMaxLength(50);
            entity.Property(e => e.IssuedDate).HasColumnType("datetime").HasDefaultValueSql("(getdate())");
            entity.Property(e => e.ExpiryDate).HasColumnType("datetime");
            entity.Property(e => e.IsRevoked).HasDefaultValue(false);

            entity.HasOne(d => d.PoliceOfficer)
                .WithMany()
                .HasForeignKey(d => d.PoliceID);

            entity.HasOne(d => d.Worker)
                .WithMany()
                .HasForeignKey(d => d.WorkerID);
        });

        modelBuilder.Entity<WorkerTimeSlots>(entity =>
        {
            entity.ToTable("WorkerTimeSlots");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.WorkerId).HasColumnName("WorkerId");
            entity.Property(e => e.StartTime).HasColumnType("time");
            entity.Property(e => e.EndTime).HasColumnType("time");
            entity.Property(e => e.IsActive).HasColumnName("IsActive").HasDefaultValue(true);

            entity.HasOne<Worker>()
                .WithMany()
                .HasForeignKey(e => e.WorkerId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ── Habits (master list + which habits a worker picked) ──
        modelBuilder.Entity<Habit>(entity =>
        {
            entity.ToTable("Habits");
            entity.HasKey(e => e.HabitId);

            entity.Property(e => e.Name).HasMaxLength(60).IsRequired();
            entity.Property(e => e.IsWorkerSelectable).HasDefaultValue(true);
            entity.Property(e => e.IsClientFilterable).HasDefaultValue(false);
            entity.Property(e => e.ExclusiveGroup).HasMaxLength(40);
            entity.HasIndex(e => e.Name).IsUnique();
        });

        modelBuilder.Entity<WorkerHabits>(entity =>
        {
            entity.ToTable("WorkerHabits");
            entity.HasKey(e => e.WorkerHabitId);

            entity.Property(e => e.CreatedDate).HasColumnType("datetime");

            // A worker can select a habit once.
            entity.HasIndex(e => new { e.WorkerId, e.HabitId }).IsUnique();

            entity.HasOne(e => e.Worker)
                .WithMany(w => w.WorkerHabits)
                .HasForeignKey(e => e.WorkerId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Habit)
                .WithMany(h => h.WorkerHabits)
                .HasForeignKey(e => e.HabitId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Course>(entity =>
        {
            entity.HasKey(e => e.CourseID);
            entity.Property(e => e.StartDate).HasColumnType("date");
            entity.Property(e => e.EndDate).HasColumnType("date");
            entity.Property(e => e.CreatedAtUtc).HasColumnType("datetime2(0)");
            entity.Property(e => e.RowVersion).IsRowVersion();
            entity.HasOne(e => e.Company).WithMany().HasForeignKey(e => e.CompanyID).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<CourseEnrollment>(entity =>
        {
            entity.HasKey(e => e.EnrollmentID);
            entity.HasIndex(e => new { e.CourseID, e.WorkerID }).IsUnique();
            entity.Property(e => e.RowVersion).IsRowVersion();
            entity.HasOne(e => e.Course).WithMany(c => c.Enrollments).HasForeignKey(e => e.CourseID).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.Worker).WithMany().HasForeignKey(e => e.WorkerID).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<CourseCertificate>(entity =>
        {
            entity.HasKey(e => e.CertificateID);
            entity.HasIndex(e => e.CertificateCode).IsUnique();
            entity.HasIndex(e => e.EnrollmentID).IsUnique();
            entity.HasIndex(e => new { e.CourseID, e.WorkerID }).IsUnique();
            entity.HasOne(e => e.Enrollment).WithOne(e => e.Certificate).HasForeignKey<CourseCertificate>(e => e.EnrollmentID).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.Course).WithMany(c => c.Certificates).HasForeignKey(e => e.CourseID).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.Worker).WithMany().HasForeignKey(e => e.WorkerID).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.Company).WithMany().HasForeignKey(e => e.CompanyID).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<WorkerNotification>(entity =>
        {
            entity.ToTable("WorkerNotification", "dbo");
            entity.HasKey(n => new { n.WorkerId, n.NotificationKey })
                .HasName("PK_WorkerNotification");
            entity.Property(n => n.NotificationKey).HasMaxLength(96).IsUnicode(false);
            entity.Property(n => n.Type).HasMaxLength(20).IsUnicode(false);
            entity.Property(n => n.Title).HasMaxLength(160);
            entity.Property(n => n.Message).HasColumnType("nvarchar(max)");
            // SQL datetime2 does not retain DateTime.Kind. Restore UTC when reading
            // so mobile clients receive a Z suffix instead of assuming local time.
            var utc = new ValueConverter<DateTime, DateTime>(value => value,
                value => DateTime.SpecifyKind(value, DateTimeKind.Utc));
            entity.Property(n => n.CreatedAtUtc).HasColumnType("datetime2(7)").HasConversion(utc);
            entity.Property(n => n.ReadAtUtc).HasColumnType("datetime2(7)").HasConversion(utc);
            entity.HasIndex(n => new { n.WorkerId, n.CreatedAtUtc, n.NotificationKey })
                .IsDescending(false, true, true).HasDatabaseName("IX_WorkerNotification_Feed");
            entity.HasIndex(n => new { n.WorkerId, n.ReadAtUtc })
                .HasDatabaseName("IX_WorkerNotification_Unread");
            entity.HasOne<Worker>().WithMany().HasForeignKey(n => n.WorkerId)
                .OnDelete(DeleteBehavior.Cascade).HasConstraintName("FK_WorkerNotification_Worker");
        });


        var utc = new ValueConverter<DateTime, DateTime>(v => v,
            v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
        modelBuilder.Entity<Resignation>(entity =>
        {
            entity.Property(r => r.NoticeEndsAtUtc).HasColumnType("datetime2(7)").HasConversion(utc);
            entity.Property(r => r.AcceptedAtUtc).HasColumnType("datetime2(7)").HasConversion(utc);
            entity.Property(r => r.AcceptanceMode).HasMaxLength(16).IsUnicode(false);
            entity.HasIndex(r => new { r.NoticeEndsAtUtc, r.ResignationId })
                .IncludeProperties(r => new { r.AcceptedAtUtc, r.InterviewId })
                .HasFilter("[NoticeEndsAtUtc] IS NOT NULL AND [AcceptedAtUtc] IS NULL")
                .HasDatabaseName("IX_Resignation_DueNotice");
            // Legacy duplicates are retained. Only newly submitted workflow rows
            // participate in this guard; one resignation per contract as before.
            entity.HasIndex(r => r.InterviewId).IsUnique()
                .HasFilter("[NoticeEndsAtUtc] IS NOT NULL AND [Interview_ID] IS NOT NULL")
                .HasDatabaseName("UX_Resignation_NewWorkflowContract");
        });


        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}