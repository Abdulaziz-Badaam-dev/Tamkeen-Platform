using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Tamkeen.API.Data;
using Tamkeen.API.Services.AiAgent.Interfaces;
using Tamkeen.API.Services.AiAgent.Models;
using Xunit;

namespace Tamkeen.API.Tests;

public sealed partial class BackendCoreTests
{
    [Fact]
    public async Task RegisterLoginAndReadProtectedProfile()
    {
        using var app = new CoreFactory();
        using var client = app.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
        await Register(client, "learner@example.test");
        var bad = await client.PostAsJsonAsync("/api/auth/login", new { email = "learner@example.test", password = "incorrect" });
        Assert.Equal(HttpStatusCode.Unauthorized, bad.StatusCode);
        var login = await Read(await client.PostAsJsonAsync("/api/auth/login", new { email = "learner@example.test", password = Password }));
        Authorize(client, login);
        var me = await Read(await client.GetAsync("/api/auth/me"));
        Assert.Equal("متدرّب تجريبي", Text(me["user"]!, "fullName"));
        Assert.Empty(me["enrollments"]!.AsArray());
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "tampered");
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task SeederIsRepeatableAndEnrollmentCreatesDeadlinesOnlyForActiveTask()
    {
        using var app = new CoreFactory();
        using var client = app.CreateClient();
        using (var scope = app.Services.CreateScope())
            scope.ServiceProvider.GetRequiredService<TamkeenDb>().Database.EnsureCreated();
        var tracks = (await Read(await client.GetAsync("/api/tracks"))).AsArray();
        Assert.Equal(new[] { "Active", "ComingSoon", "ComingSoon" }, tracks.Select(t => Text(t!, "status")));
        Assert.Equal(new[] { 3, 0, 0 }, tracks.Select(t => (int)t!["taskCount"]!));
        var track = (int)tracks[0]!["id"]!;
        await Register(client, "enroll@example.test");
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync($"/api/tracks/{tracks[1]!["id"]}/enroll", null)).StatusCode);
        await Read(await client.PostAsync($"/api/tracks/{track}/enroll", null));
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync($"/api/tracks/{track}/enroll", null)).StatusCode);
        var tasks = (await Progress(client, track))["tasks"]!.AsArray();
        Assert.Equal(new[] { "Active", "Locked", "Locked" }, tasks.Select(t => Text(t!, "status")));
        CheckDeadline(tasks[0]!, 24);
        Assert.Null(tasks[1]!["deadlineAt"]);
        Assert.Equal(HttpStatusCode.Conflict, (await Submit(client, (int)tasks[1]!["taskId"]!, "pass-two")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"/api/tasks/{tasks[0]!["taskId"]}/submit-github",
            new { repoUrl = "https://github.com/team/project", branch = "main" })).StatusCode);
    }

    [Fact]
    public async Task RetryUsesDatabaseFeedbackThenFastTracksToCertificate()
    {
        using var app = new CoreFactory();
        using var client = app.CreateClient();
        await Register(client, "complete@example.test");
        var track = (int)(await Read(await client.GetAsync("/api/tracks")))[0]!["id"]!;
        await Read(await client.PostAsync($"/api/tracks/{track}/enroll", null));
        var tasks = (await Progress(client, track))["tasks"]!.AsArray();
        int[] ids = tasks.Select(t => (int)t!["taskId"]!).ToArray();
        var fail = await Read(await Submit(client, ids[0], "retry-me"));
        Assert.Equal("Rejected", Text(fail, "status"));
        Assert.Equal(1, (int)fail["attemptNumber"]!);
        Assert.True((bool)fail["isOnTime"]!);
        tasks = (await Progress(client, track))["tasks"]!.AsArray();
        Assert.Equal("Active", Text(tasks[0]!, "status"));
        Assert.Equal("Locked", Text(tasks[1]!, "status"));
        Assert.Equal("Rejected", Text(tasks[0]!["latestEvaluation"]!, "status"));

        var first = await Read(await Submit(client, ids[0], "pass-one"));
        Assert.Equal(2, (int)first["attemptNumber"]!);
        Assert.Equal(70m, (decimal)first["overallScore"]!);
        Assert.True(app.Model.RetrySawPriorIssue);
        tasks = (await Progress(client, track))["tasks"]!.AsArray();
        Assert.Equal("Completed", Text(tasks[0]!, "status"));
        Assert.Equal("Active", Text(tasks[1]!, "status"));
        CheckDeadline(tasks[1]!, 24);
        Assert.Equal(2, (await Read(await client.GetAsync($"/api/tasks/{ids[0]}")))["submissions"]!.AsArray().Count);
        await Read(await Submit(client, ids[1], "pass-two"));
        CheckDeadline((await Progress(client, track))["tasks"]![2]!, 168);
        var last = await Read(await client.PostAsJsonAsync($"/api/tasks/{ids[2]}/submit-github",
            new { repoUrl = "https://github.com/team/project", branch = "main" }));
        Assert.Equal("GitHub", Text(last, "submissionType"));
        Assert.Equal(100m, (decimal)last["overallScore"]!);
        var progress = await Progress(client, track);
        Assert.Equal("Completed", Text(progress, "status"));
        Assert.All(progress["tasks"]!.AsArray(), t => Assert.Equal("Completed", Text(t!, "status")));
        var final = progress["finalEvaluation"]!;
        Assert.Equal(82.33m, (decimal)final["overallScore"]!);
        Assert.Equal(90m, (decimal)final["logic"]!);
        Assert.Equal(80m, (decimal)final["security"]!);
        Assert.Equal(83.33m, (decimal)final["codeQuality"]!);
        Assert.Equal(70m, (decimal)final["compliance"]!);
        Assert.False(string.IsNullOrEmpty(Text(final, "recommendation")));
        var certificates = (await Read(await client.GetAsync("/api/certificates/my"))).AsArray();
        var cert = Assert.Single(certificates)!;
        string serial = Text(cert["certificate"]!, "serialNumber");
        Assert.Matches("^TMK-2026-[A-Z0-9]{8}$", serial);
        var pdf = await client.GetAsync($"/api/certificates/{serial}/pdf");
        Assert.Equal("application/pdf", pdf.Content.Headers.ContentType!.MediaType);
        Assert.StartsWith("%PDF-", Encoding.ASCII.GetString(await pdf.Content.ReadAsByteArrayAsync(), 0, 5));
        Assert.Equal(HttpStatusCode.Conflict, (await Submit(client, ids[2], "again")).StatusCode);
        client.DefaultRequestHeaders.Authorization = null;
        var verified = await Read(await client.GetAsync($"/api/certificates/verify/{serial}"));
        Assert.True((bool)verified["authentic"]!);
        Assert.Equal("متدرّب تجريبي", Text(verified["certificate"]!, "traineeName"));
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/certificates/verify/TMK-2026-NOTVALID")).StatusCode);
        await Register(client, "other@example.test");
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/submissions/{last["id"]}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/certificates/{serial}/pdf")).StatusCode);
        Assert.Empty((await Read(await client.GetAsync("/api/certificates/my"))).AsArray());
    }

    [Fact]
    public async Task ProviderFailureDoesNotConsumeAnAttemptAndLateRetryIsRecorded()
    {
        using var app = new CoreFactory();
        using var client = app.CreateClient();
        await Register(client, "late@example.test");
        var track = (int)(await Read(await client.GetAsync("/api/tracks")))[0]!["id"]!;
        await Read(await client.PostAsync($"/api/tracks/{track}/enroll", null));
        int task = (int)(await Progress(client, track))["tasks"]![0]!["taskId"]!;
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TamkeenDb>();
            (await db.Progress.SingleAsync(p => p.TaskId == task)).DeadlineAt = DateTimeOffset.UtcNow.AddHours(-1);
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.BadGateway, (await Submit(client, task, "provider-failure")).StatusCode);
        var report = await Read(await Submit(client, task, "retry-me"));
        Assert.Equal(1, (int)report["attemptNumber"]!);
        Assert.False((bool)report["isOnTime"]!);
        Assert.Equal(report["id"]!.ToString(), (await Read(await client.GetAsync($"/api/submissions/{report["id"]}")))["id"]!.ToString());
    }

    private const string Password = "Test-Fixture42!";
    private static string Text(JsonNode node, string key) => node[key]!.GetValue<string>();
    private static void CheckDeadline(JsonNode task, int hours) =>
        Assert.Equal(TimeSpan.FromHours(hours), DateTimeOffset.Parse(Text(task, "deadlineAt")) - DateTimeOffset.Parse(Text(task, "unlockedAt")));
    private static async Task<JsonNode> Progress(HttpClient client, int track) =>
        await Read(await client.GetAsync($"/api/tracks/{track}/my-progress"));
    private static async Task Register(HttpClient client, string email) => Authorize(client,
        await Read(await client.PostAsJsonAsync("/api/auth/register", new { fullName = "متدرّب تجريبي", email, password = Password })));
    private static void Authorize(HttpClient client, JsonNode json) =>
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Text(json, "accessToken"));
    private static async Task<JsonNode> Read(HttpResponseMessage response)
    {
        string body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{response.StatusCode}: {body}");
        return JsonNode.Parse(body)!;
    }
    private static async Task<HttpResponseMessage> Submit(HttpClient client, int task, string text)
    {
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(text), "Files", "demo.py");
        return await client.PostAsync($"/api/tasks/{task}/submit-files", form);
    }

    private sealed class CoreFactory : WebApplicationFactory<Program>
    {
        private readonly string database = Path.Combine(Path.GetTempPath(), $"tamkeen-{Guid.NewGuid():N}.db");
        public FixtureModel Model { get; } = new();
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Key"] = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)),
                ["Jwt:Issuer"] = "test", ["Jwt:Audience"] = "test-client", ["Jwt:ExpiryMinutes"] = "20"
            }));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IDbContextOptionsConfiguration<TamkeenDb>>();
                services.AddDbContext<TamkeenDb>(options => options.UseSqlite($"Data Source={database};Pooling=False"));
                services.AddSingleton<IAiModelClient>(Model);
                services.AddHttpClient("github").ConfigurePrimaryHttpMessageHandler(() => new GitHubFixture());
            });
        }
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing) File.Delete(database);
        }
    }

    private sealed class FixtureModel : IAiModelClient
    {
        public bool RetrySawPriorIssue { get; private set; }
        public Task<JsonObject> GenerateAsync(JsonObject request, CancellationToken ct)
        {
            var turns = request["contents"]!.AsArray();
            if (request["tools"] != null)
            {
                if (turns.Count == 1) return Part(new { functionCall = new { name = "get_previous_attempt_issues" } });
                RetrySawPriorIssue |= turns.Last()!.ToString().Contains("feedback-A01");
                return Part(new { text = "Reviewed previous feedback." });
            }
            var prompt = turns[0]!["parts"]![0]!["text"]!.GetValue<string>();
            if (prompt.Contains("provider-failure")) throw new EvaluationException(502, "Model unavailable.");
            bool rejected = prompt.Contains("retry-me");
            var scores = rejected ? new Scores(20, 40, 30, 10) : prompt.Contains("pass-one") ? new Scores(80, 60, 80, 50) :
                prompt.Contains("pass-two") ? new Scores(90, 80, 70, 60) : new Scores(100, 100, 100, 100);
            Issue[] issues = rejected ? [new("logic", "low", "demo.py", 1, "تحتاج الحالة إلى معالجة.", "feedback-A01")] : [];
            var report = new ModelReport(scores, "ملخص تجريبي", ["تنظيم واضح"], issues);
            return Part(new { text = JsonSerializer.Serialize(report, JsonSerializerOptions.Web) });
        }
        private static Task<JsonObject> Part(object part) => Task.FromResult(new JsonObject
            { ["role"] = "model", ["parts"] = new JsonArray(JsonSerializer.SerializeToNode(part)) });
    }

    private sealed class GitHubFixture : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            object data = request.RequestUri!.AbsolutePath switch
            {
                "/repos/team/project" => new { visibility = "public" },
                "/repos/team/project/git/ref/heads/main" => new { @ref = "refs/heads/main", @object = new { sha = "commit" } },
                "/repos/team/project/git/commits/commit" => new { tree = new { sha = "tree" } },
                "/repos/team/project/git/trees/tree" => new { truncated = false, tree = new[] { new { mode = "100644", path = "api.py", size = 4, sha = "blob" } } },
                "/repos/team/project/git/blobs/blob" => new { content = Convert.ToBase64String("pass"u8.ToArray()) },
                _ => throw new InvalidOperationException("Unexpected GitHub request.")
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(data) });
        }
    }
}
