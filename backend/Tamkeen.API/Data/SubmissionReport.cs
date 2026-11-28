using System.Text.Json;
using Tamkeen.API.Data.Entities;
using Tamkeen.API.Services.AiAgent.Models;

namespace Tamkeen.API.Data;

public static class SubmissionReport
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public static Issue[] ReadIssues(string json) => JsonSerializer.Deserialize<Issue[]>(json, Json)!;
    public static object View(Entities.Submission s) => new
    {
        s.Id, s.TaskId, s.AttemptNumber, s.SubmissionType, s.GitHubRepoUrl, s.GitHubBranch, s.SubmittedAt, s.IsOnTime,
        s.OverallScore, s.Status, scores = new Scores(s.LogicScore, s.SecurityScore, s.CodeQualityScore, s.ComplianceScore),
        summary = s.SummaryAr, strengths = JsonSerializer.Deserialize<string[]>(s.StrengthsJson, Json), issues = ReadIssues(s.IssuesJson)
    };
    public static object Final(Enrollment e) => new { overallScore = e.FinalOverallScore,
        logic = e.FinalLogicScore, security = e.FinalSecurityScore, codeQuality = e.FinalCodeQualityScore,
        compliance = e.FinalComplianceScore, recommendation = e.FinalRecommendationAr };
}
