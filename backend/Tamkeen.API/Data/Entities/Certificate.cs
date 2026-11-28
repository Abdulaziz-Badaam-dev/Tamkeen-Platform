using System.ComponentModel.DataAnnotations;

namespace Tamkeen.API.Data.Entities;

public sealed class Certificate
{
    public int Id { get; set; }
    public int EnrollmentId { get; set; }
    public Enrollment Enrollment { get; set; } = null!;
    public required string UserId { get; set; }
    public int TrackId { get; set; }
    [MaxLength(17)] public required string SerialNumber { get; set; }
    public required string TraineeName { get; set; }
    public required string TrackTitleAr { get; set; }
    public decimal FinalOverallScore { get; set; }
    public DateTimeOffset IssuedAt { get; set; }
}
