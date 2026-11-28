using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Tamkeen.API.Data;
using Tamkeen.API.Data.Entities;
using Tamkeen.API.Services.AiAgent.Models;
using StoredSubmission = Tamkeen.API.Data.Entities.Submission;

namespace Tamkeen.API.Services;

public sealed class TrackWorkflow(TamkeenDb db)
{
    public async Task<StoredSubmission> RecordAsync(UserTaskProgress progress, string userId, DateTimeOffset received,
        EvaluationResultDto report, string? repo, string? branch, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var record = new StoredSubmission
        {
            UserTaskProgressId = progress.Id, TaskId = progress.TaskId, UserId = userId,
            AttemptNumber = ++progress.AttemptCount, SubmittedAt = received, IsOnTime = received <= progress.DeadlineAt,
            SubmissionType = repo == null ? "Files" : "GitHub", GitHubRepoUrl = repo, GitHubBranch = branch,
            OverallScore = report.OverallScore, Status = report.Status, SummaryAr = report.Summary,
            LogicScore = report.Scores.Logic, SecurityScore = report.Scores.Security, CodeQualityScore = report.Scores.CodeQuality,
            ComplianceScore = report.Scores.Compliance, StrengthsJson = JsonSerializer.Serialize(report.Strengths),
            IssuesJson = JsonSerializer.Serialize(report.Issues)
        };
        db.Submissions.Add(record);
        progress.BestScore = Math.Max(progress.BestScore ?? 0, report.OverallScore);
        if (report.Status == "Completed")
        {
            progress.Status = "Completed";
            progress.CompletedAt = now;
            var next = await db.Progress.Include(p => p.Task).Where(p => p.EnrollmentId == progress.EnrollmentId &&
                p.Task.OrderIndex > progress.Task.OrderIndex).OrderBy(p => p.Task.OrderIndex).FirstOrDefaultAsync(ct);
            if (next != null) next.Unlock(now, next.Task.DurationHours);
            else await CompleteAsync(progress.Enrollment, record, now, ct);
        }
        // EF commits the report, optimistic attempt counter, unlock and certificate in one transaction.
        await db.SaveChangesAsync(ct);
        return record;
    }

    private async Task CompleteAsync(Enrollment enrollment, StoredSubmission last, DateTimeOffset now, CancellationToken ct)
    {
        var completed = await db.Progress.Where(p => p.EnrollmentId == enrollment.Id)
            .SelectMany(p => p.Submissions).Where(s => s.Status == "Completed").AsNoTracking().ToListAsync(ct);
        completed.Add(last);
        decimal Mean(Func<StoredSubmission, decimal> score) => Math.Round(completed.Average(score), 2, MidpointRounding.AwayFromZero);
        enrollment.FinalOverallScore = Mean(s => s.OverallScore);
        enrollment.FinalLogicScore = Mean(s => s.LogicScore);
        enrollment.FinalSecurityScore = Mean(s => s.SecurityScore);
        enrollment.FinalCodeQualityScore = Mean(s => s.CodeQualityScore);
        enrollment.FinalComplianceScore = Mean(s => s.ComplianceScore);
        var recommendations = new (decimal? Score, string Text)[] {
            (enrollment.FinalLogicScore, "واصل اختبار الحالات الحدّية قبل تسليم الحل."),
            (enrollment.FinalSecurityScore, "راجع حدود الثقة وآليات التحقق من المدخلات في مشروعك القادم."),
            (enrollment.FinalCodeQualityScore, "ركّز على وضوح الأسماء وفصل المسؤوليات في مشروعك القادم."),
            (enrollment.FinalComplianceScore, "اربط متطلبات المهمة بقائمة تحقق وراجعها قبل الموعد.")
        };
        enrollment.FinalRecommendationAr = "أتممت المسار بنجاح. " + recommendations.MinBy(r => r.Score).Text;
        enrollment.Status = "Completed";
        enrollment.CompletedAt = now;
        string serial;
        do
        {
            serial = "TMK-2026-" + RandomNumberGenerator.GetString("ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789", 8);
        } while (await db.Certificates.AnyAsync(c => c.SerialNumber == serial, ct));
        db.Certificates.Add(new Certificate
        {
            EnrollmentId = enrollment.Id, UserId = enrollment.UserId, TrackId = enrollment.TrackId,
            SerialNumber = serial, TraineeName = await db.Users.Where(u => u.Id == enrollment.UserId).Select(u => u.FullName).SingleAsync(ct),
            TrackTitleAr = enrollment.Track.TitleAr, IssuedAt = now, FinalOverallScore = enrollment.FinalOverallScore.Value
        });
    }
}
