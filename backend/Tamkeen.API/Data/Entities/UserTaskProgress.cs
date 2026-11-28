using System.ComponentModel.DataAnnotations;

namespace Tamkeen.API.Data.Entities;

public sealed class UserTaskProgress
{
    public int Id { get; set; }
    public int EnrollmentId { get; set; }
    public Enrollment Enrollment { get; set; } = null!;
    public int TaskId { get; set; }
    public TrackTask Task { get; set; } = null!;
    public string Status { get; set; } = "Locked";
    public DateTimeOffset? UnlockedAt { get; set; }
    public DateTimeOffset? DeadlineAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public decimal? BestScore { get; set; }
    [ConcurrencyCheck] public int AttemptCount { get; set; }
    public List<Submission> Submissions { get; set; } = [];

    public void Unlock(DateTimeOffset now, int durationHours)
    {
        Status = "Active";
        UnlockedAt = now;
        DeadlineAt = now.AddHours(durationHours);
    }
}
