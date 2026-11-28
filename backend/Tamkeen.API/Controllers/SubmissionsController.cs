using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Tamkeen.API.Data;
using Tamkeen.API.Services;
using Tamkeen.API.Services.AiAgent;
using Tamkeen.API.Services.AiAgent.Interfaces;
using Tamkeen.API.Services.AiAgent.Models;
using Tamkeen.API.Services.Auth;

namespace Tamkeen.API.Controllers;

public sealed class TaskUpload
{
    [Required] public required List<IFormFile> Files { get; init; }
}
public sealed record TaskRepository([Required, StringLength(2048)] string RepoUrl, [Required, StringLength(255)] string Branch);

[Authorize, ApiController]
public sealed class SubmissionsController(TamkeenDb db, SubmissionReader reader, IEvaluationAgentService agent,
    TrackWorkflow workflow) : ControllerBase
{
    [HttpPost("api/tasks/{taskId:int}/submit-files"), EnableRateLimiting("evaluation"), Consumes("multipart/form-data")]
    [RequestSizeLimit(SubmissionReader.MaxBytes + 65536)]
    public Task<IActionResult> Files(int taskId, [FromForm] TaskUpload input, CancellationToken ct) =>
        Submit(taskId, (metadata, token) => reader.FromFilesAsync(metadata, input.Files, token), null, null, ct);

    [HttpPost("api/tasks/{taskId:int}/submit-github"), EnableRateLimiting("evaluation"), RequestSizeLimit(65536)]
    public Task<IActionResult> GitHub(int taskId, TaskRepository input, CancellationToken ct) =>
        Submit(taskId, (metadata, token) => reader.FromGitHubAsync(metadata, input.RepoUrl, input.Branch, token),
            input.RepoUrl, input.Branch, ct);

    [HttpGet("api/submissions/{submissionId:int}")]
    public async Task<IActionResult> Get(int submissionId, CancellationToken ct)
    {
        var id = User.UserId();
        var record = await db.Submissions.AsNoTracking().SingleOrDefaultAsync(s => s.Id == submissionId && s.UserId == id, ct);
        return record == null ? NotFound() : Ok(SubmissionReport.View(record));
    }

    private async Task<IActionResult> Submit(int taskId, Func<SubmissionInput, CancellationToken, Task<Submission>> read,
        string? repo, string? branch, CancellationToken ct)
    {
        var userId = User.UserId();
        var received = DateTimeOffset.UtcNow;
        var progress = await db.Progress.Include(p => p.Task).Include(p => p.Enrollment).ThenInclude(e => e.Track)
            .SingleOrDefaultAsync(p => p.TaskId == taskId && p.Enrollment.UserId == userId, ct);
        if (progress == null) return NotFound();
        if (progress.Status != "Active") return Problem(statusCode: 409, detail: "Only active tasks accept submissions.");
        var task = progress.Task;
        if (repo != null && task.AllowedSubmissionMode != "GitHubOrFiles")
            return Problem(statusCode: 400, detail: "This task accepts files only.");
        var metadata = new SubmissionInput(string.Join("\n", task.TitleAr, task.DescriptionAr, task.RequirementsAr),
            progress.AttemptCount + 1, received, progress.DeadlineAt);
        EvaluationResultDto report;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromMinutes(3));
        try
        {
            var source = await read(metadata, timeout.Token);
            Response.Headers["X-Submission-Truncated"] = source.Truncated ? "true" : "false";
            report = await agent.EvaluateAsync(source with { UserId = userId, TaskId = taskId }, timeout.Token);
        }
        catch (EvaluationException ex) { return Problem(statusCode: ex.Status, detail: ex.Message); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return Problem(statusCode: 504, detail: "Evaluation timed out."); }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or FormatException or KeyNotFoundException or InvalidOperationException)
        { return Problem(statusCode: 502, detail: "Invalid evaluation provider response."); }
        var record = await workflow.RecordAsync(progress, userId, received, report, repo, branch, ct);
        return Ok(SubmissionReport.View(record));
    }
}
