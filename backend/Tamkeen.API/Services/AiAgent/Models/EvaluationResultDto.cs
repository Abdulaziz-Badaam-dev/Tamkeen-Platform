namespace Tamkeen.API.Services.AiAgent.Models;

public sealed record Scores(decimal Logic, decimal Security, decimal CodeQuality, decimal Compliance);
public sealed record Issue(string Criterion, string Severity, string? File, int? Line, string Explanation, string Suggestion);
public sealed record ModelReport(Scores Scores, string Summary, string[] Strengths, Issue[] Issues);
public sealed record EvaluationResultDto(decimal OverallScore, string Status, Scores Scores, string Summary, string[] Strengths, Issue[] Issues);
public sealed record SourceFile(string Name, string Text);
public sealed record Submission(string Task, int Attempt, DateTimeOffset Received, DateTimeOffset? Due,
    IReadOnlyList<SourceFile> Files, string NumberedCode, bool Truncated,
    int CollectedFileCount, int CollectedCharacters, string[] CollectedExtensions)
{
    public string? UserId { get; init; }
    public int? TaskId { get; init; }
}
public sealed class EvaluationException(int status, string message) : Exception(message)
{
    public int Status { get; } = status;
}
