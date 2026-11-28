using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Tamkeen.API.Data.Entities;

namespace Tamkeen.API.Data;

public sealed class TamkeenDb(DbContextOptions<TamkeenDb> options) : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<Track> Tracks => Set<Track>();
    public DbSet<TrackTask> Tasks => Set<TrackTask>();
    public DbSet<Enrollment> Enrollments => Set<Enrollment>();
    public DbSet<UserTaskProgress> Progress => Set<UserTaskProgress>();
    public DbSet<Submission> Submissions => Set<Submission>();
    public DbSet<Certificate> Certificates => Set<Certificate>();

    protected override void ConfigureConventions(ModelConfigurationBuilder builder) =>
        builder.Properties<decimal>().HavePrecision(12, 6);

    protected override void OnConfiguring(DbContextOptionsBuilder options) =>
        options.UseSeeding((db, _) => DbSeeder.Seed((TamkeenDb)db));

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);
        b.Entity<ApplicationUser>().HasIndex(u => u.GoogleSubjectId).IsUnique();
        b.Entity<ApplicationUser>().HasIndex(u => u.NormalizedEmail).IsUnique();
        b.Entity<Track>().HasIndex(t => t.Slug).IsUnique();
        b.Entity<TrackTask>().HasIndex(t => new { t.TrackId, t.OrderIndex }).IsUnique();
        b.Entity<Enrollment>().HasIndex(e => new { e.UserId, e.TrackId }).IsUnique();
        b.Entity<UserTaskProgress>().HasIndex(p => new { p.EnrollmentId, p.TaskId }).IsUnique();
        b.Entity<Submission>().HasIndex(s => new { s.UserTaskProgressId, s.AttemptNumber }).IsUnique();
        b.Entity<Certificate>().HasIndex(c => c.SerialNumber).IsUnique();
        b.Entity<Certificate>().HasIndex(c => c.EnrollmentId).IsUnique();

        b.Entity<Enrollment>().HasOne<ApplicationUser>().WithMany().HasForeignKey(e => e.UserId);
        b.Entity<UserTaskProgress>().HasOne(p => p.Task).WithMany().HasForeignKey(p => p.TaskId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Submission>().HasOne<UserTaskProgress>().WithMany(p => p.Submissions).HasForeignKey(s => s.UserTaskProgressId);
        b.Entity<Submission>().HasOne<ApplicationUser>().WithMany().HasForeignKey(s => s.UserId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Submission>().HasOne<TrackTask>().WithMany().HasForeignKey(s => s.TaskId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Certificate>().HasOne<ApplicationUser>().WithMany().HasForeignKey(c => c.UserId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Certificate>().HasOne<Track>().WithMany().HasForeignKey(c => c.TrackId).OnDelete(DeleteBehavior.Restrict);
    }
}
