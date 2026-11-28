namespace Tamkeen.API.Data.Entities;

public sealed class Submission
{
    public int Id { get; set; }
    public int UserTaskProgressId { get; set; }
    public required string UserId { get; set; }
    public int TaskId { get; set; }
    public int AttemptNumber { get; set; }
    public required string SubmissionType { get; set; }
    public string? GitHubRepoUrl { get; set; }
    public string? GitHubBranch { get; set; }
    public DateTimeOffset SubmittedAt { get; set; }
    public bool IsOnTime { get; set; }
    public decimal OverallScore { get; set; }
    public required string Status { get; set; }
    public decimal LogicScore { get; set; }
    public decimal SecurityScore { get; set; }
    public decimal CodeQualityScore { get; set; }
    public decimal ComplianceScore { get; set; }
    public required string SummaryAr { get; set; }
    public required string StrengthsJson { get; set; }
    public required string IssuesJson { get; set; }
}
