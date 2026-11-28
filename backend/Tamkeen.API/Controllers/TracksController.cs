using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Tamkeen.API.Data;
using Tamkeen.API.Data.Entities;
using Tamkeen.API.Services.Auth;

namespace Tamkeen.API.Controllers;

[ApiController, Route("api/tracks")]
public sealed class TracksController(TamkeenDb db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct) =>
        Ok(await db.Tracks.OrderBy(t => t.DisplayOrder).Select(t => new
        { t.Id, t.Slug, t.TitleEn, t.TitleAr, t.DescriptionAr, t.Status, t.DisplayOrder, taskCount = t.Tasks.Count }).ToListAsync(ct));

    [Authorize, HttpPost("{trackId:int}/enroll")]
    public async Task<IActionResult> Enroll(int trackId, CancellationToken ct)
    {
        var track = await db.Tracks.Include(t => t.Tasks).SingleOrDefaultAsync(t => t.Id == trackId, ct);
        if (track == null) return NotFound();
        if (track.Status != "Active") return Conflict(new { detail = "Track is coming soon." });
        var userId = User.UserId();
        if (await db.Enrollments.AnyAsync(e => e.TrackId == trackId && e.UserId == userId, ct))
            return Conflict(new { detail = "Already enrolled." });
        var now = DateTimeOffset.UtcNow;
        var enrollment = new Enrollment { UserId = userId, TrackId = trackId, EnrolledAt = now };
        foreach (var task in track.Tasks.OrderBy(t => t.OrderIndex))
        {
            var progress = new UserTaskProgress { TaskId = task.Id };
            if (enrollment.Progress.Count == 0) progress.Unlock(now, task.DurationHours);
            enrollment.Progress.Add(progress);
        }
        db.Enrollments.Add(enrollment);
        await db.SaveChangesAsync(ct);
        return Ok(new { enrollment.Id, enrollment.TrackId, enrollment.Status, enrollment.EnrolledAt });
    }

    [Authorize, HttpGet("{trackId:int}/my-progress")]
    public async Task<IActionResult> Progress(int trackId, CancellationToken ct)
    {
        var userId = User.UserId();
        var e = await db.Enrollments.AsNoTracking().Where(e => e.TrackId == trackId && e.UserId == userId)
            .Include(e => e.Progress).ThenInclude(p => p.Task)
            .Include(e => e.Progress).ThenInclude(p => p.Submissions.OrderByDescending(s => s.AttemptNumber).Take(1))
            .AsSplitQuery().SingleOrDefaultAsync(ct);
        if (e == null) return NotFound();
        return Ok(new { e.Id, e.TrackId, e.Status, e.EnrolledAt, e.CompletedAt, finalEvaluation = SubmissionReport.Final(e),
            tasks = e.Progress.OrderBy(p => p.Task.OrderIndex).Select(p => new { p.TaskId, p.Task.OrderIndex, p.Task.TitleAr,
                p.Task.TaskType, p.Task.DurationHours, p.Task.AllowedSubmissionMode, p.Status, p.UnlockedAt, p.DeadlineAt,
                p.CompletedAt, p.BestScore, p.AttemptCount, latestEvaluation = p.Submissions.Select(SubmissionReport.View).SingleOrDefault() }) });
    }

    [Authorize, HttpGet("/api/tasks/{taskId:int}")]
    public async Task<IActionResult> TaskDetails(int taskId, CancellationToken ct)
    {
        var userId = User.UserId();
        var p = await db.Progress.AsNoTracking().Include(p => p.Task).Include(p => p.Submissions)
            .SingleOrDefaultAsync(p => p.TaskId == taskId && p.Enrollment.UserId == userId, ct);
        if (p == null) return NotFound();
        return Ok(new { task = p.Task, p.Status, p.UnlockedAt, p.DeadlineAt, p.CompletedAt, p.BestScore, p.AttemptCount,
            submissions = p.Submissions.OrderByDescending(s => s.AttemptNumber).Select(SubmissionReport.View) });
    }
}
