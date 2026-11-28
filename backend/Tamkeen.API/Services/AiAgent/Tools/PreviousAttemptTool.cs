using Microsoft.EntityFrameworkCore;
using Tamkeen.API.Data;
using Tamkeen.API.Services.AiAgent.Interfaces;
using Tamkeen.API.Services.AiAgent.Models;
namespace Tamkeen.API.Services.AiAgent.Tools;

public interface IPreviousAttempts
{
    IReadOnlyList<Issue> LatestRejected(Submission submission);
}

public sealed class DatabasePreviousAttempts(TamkeenDb db) : IPreviousAttempts
{
    public IReadOnlyList<Issue> LatestRejected(Submission submission)
    {
        if (submission.UserId == null || submission.TaskId == null || submission.Attempt <= 1) return [];
        var json = db.Submissions.AsNoTracking()
            .Where(s => s.UserId == submission.UserId && s.TaskId == submission.TaskId &&
                s.Status == "Rejected" && s.AttemptNumber < submission.Attempt)
            .OrderByDescending(s => s.AttemptNumber).Select(s => s.IssuesJson).FirstOrDefault();
        return json == null ? [] : SubmissionReport.ReadIssues(json);
    }
}

public sealed class PreviousAttemptTool(IPreviousAttempts attempts) : IAgentTool
{
    public string Name => "get_previous_attempt_issues";
    public string Description => "Get the previous rejected attempt's issues for this trainee and task.";
    public object Read(Submission submission) => attempts.LatestRejected(submission);
}
