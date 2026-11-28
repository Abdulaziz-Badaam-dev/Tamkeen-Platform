namespace Tamkeen.API.Data.Entities;

public sealed class Enrollment
{
    public int Id { get; set; }
    public required string UserId { get; set; }
    public int TrackId { get; set; }
    public Track Track { get; set; } = null!;
    public string Status { get; set; } = "InProgress";
    public DateTimeOffset EnrolledAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public decimal? FinalOverallScore { get; set; }
    public decimal? FinalLogicScore { get; set; }
    public decimal? FinalSecurityScore { get; set; }
    public decimal? FinalCodeQualityScore { get; set; }
    public decimal? FinalComplianceScore { get; set; }
    public string? FinalRecommendationAr { get; set; }
    public List<UserTaskProgress> Progress { get; set; } = [];
}
