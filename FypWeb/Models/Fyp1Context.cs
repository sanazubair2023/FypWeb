using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;

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

            // Optional link to the picked time slot. SetNull matches the
            // ON DELETE SET NULL foreign key created by Add_SlotId_To_Interview.sql,
            // so deleting a slot never breaks an existing interview.
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
            entity.Property(e => e.ReviewId).HasColumnName("Review_ID");
            entity.Property(e => e.Comment).HasColumnType("text");
            entity.Property(e => e.InterviewId).HasColumnName("Interview_ID");
            entity.Property(e => e.ReviewDate).HasDefaultValueSql("(getdate())").HasColumnType("datetime");
            entity.Property(e => e.ReviewerRole)
                .HasMaxLength(50)
                .IsUnicode(false)
                .HasDefaultValue("Client");

            entity.HasOne(d => d.Interview).WithMany(p => p.Reviews)
                .HasForeignKey(d => d.InterviewId)
                .HasConstraintName("FK__Reviews__Intervi__5FB337D6");
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
            entity.Property(e => e.Latitude).HasColumnName("Latitude");
            entity.Property(e => e.Longitude).HasColumnName("Longitude");
            entity.Property(e => e.Radius).HasColumnName("Radius").HasDefaultValue(5);
        });

        modelBuilder.Entity<Hiring>(entity =>
        {
            entity.ToTable("Hiring");

            entity.HasKey(e => e.HiringId);

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
                .HasColumnName("Hiring_Date");

            entity.HasOne(d => d.Interview)
                .WithMany(p => p.Hirings)
                .HasForeignKey(d => d.InterviewId)
                .HasConstraintName("FK__Hiring__intervie__xxxxxx");
        });

        modelBuilder.Entity<Company>(entity =>
        {
            entity.ToTable("Companies");
            entity.HasKey(e => e.CompanyID);

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

            entity.Property(e => e.WorkerID).HasColumnName("WorkerID");
            entity.Property(e => e.PoliceID).HasColumnName("PoliceID");
            entity.Property(e => e.CertificateNo).HasMaxLength(50);
            entity.Property(e => e.CharacterStatus).HasMaxLength(50);
            entity.Property(e => e.Remarks).HasColumnType("nvarchar(max)");
            entity.Property(e => e.VerifiedCnic).HasMaxLength(20);
            entity.Property(e => e.CnicVerified);
            entity.Property(e => e.IssuingStation).HasMaxLength(150);
            entity.Property(e => e.IssuingBadge).HasMaxLength(50);
            entity.Property(e => e.IssuedDate).HasColumnType("datetime").HasDefaultValueSql("(getdate())");
            entity.Property(e => e.ExpiryDate).HasColumnType("datetime");
            entity.Property(e => e.IsRevoked);

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

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}