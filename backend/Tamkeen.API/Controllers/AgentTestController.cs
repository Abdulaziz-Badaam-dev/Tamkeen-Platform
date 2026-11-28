using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Tamkeen.API.Services.AiAgent;
using Tamkeen.API.Services.AiAgent.Interfaces;
using Tamkeen.API.Services.AiAgent.Models;

namespace Tamkeen.API.Controllers;

public class EvaluationInput
{
    [Required, StringLength(6000)] public required string TaskDescription { get; init; }
    [Range(1, int.MaxValue)] public int AttemptNumber { get; init; } = 1;
    public DateTimeOffset? DueAtUtc { get; init; }
}
public sealed class UploadInput : EvaluationInput
{
    [Required] public required List<IFormFile> Files { get; init; }
}
public sealed class RepositoryInput : EvaluationInput
{
    [Required, StringLength(2048)] public required string RepoUrl { get; init; }
    [Required, StringLength(255)] public required string Branch { get; init; }
}

[ApiController, Route("api/agent-test"), EnableRateLimiting("evaluation")]
public sealed class AgentTestController(SubmissionReader reader, IEvaluationAgentService agent) : ControllerBase
{
    [HttpPost("evaluate-files"), Consumes("multipart/form-data")]
    [RequestSizeLimit(SubmissionReader.MaxBytes + 65536)]
    public Task<ActionResult<EvaluationResultDto>> Files([FromForm] UploadInput data, CancellationToken ct) =>
        Evaluate(data, (input, token) => reader.FromFilesAsync(input, data.Files, token), ct);

    [HttpPost("evaluate-github"), Consumes("application/json"), RequestSizeLimit(65536)]
    public Task<ActionResult<EvaluationResultDto>> GitHub([FromBody] RepositoryInput data, CancellationToken ct) =>
        Evaluate(data, (input, token) => reader.FromGitHubAsync(input, data.RepoUrl, data.Branch, token), ct);

    private async Task<ActionResult<EvaluationResultDto>> Evaluate(EvaluationInput data,
        Func<SubmissionInput, CancellationToken, Task<Submission>> read, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromMinutes(3));
        try
        {
            var input = new SubmissionInput(data.TaskDescription, data.AttemptNumber, DateTimeOffset.UtcNow, data.DueAtUtc);
            var submission = await read(input, deadline.Token);
            Response.Headers["X-Submission-Truncated"] = submission.Truncated ? "true" : "false";
            return Ok(await agent.EvaluateAsync(submission, deadline.Token));
        }
        catch (EvaluationException ex) { return Problem(statusCode: ex.Status, detail: ex.Message); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        { return Problem(statusCode: 504, detail: "Upstream evaluation timed out."); }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or FormatException or KeyNotFoundException or InvalidOperationException)
        { return Problem(statusCode: 502, detail: "Could not obtain a valid upstream response."); }
    }
}
