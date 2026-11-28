using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Tamkeen.API.Services.AiAgent;
using Tamkeen.API.Services.AiAgent.Clients;
using Tamkeen.API.Services.AiAgent.Models;
using Tamkeen.API.Services.AiAgent.Tools;
using Xunit;

namespace Tamkeen.API.Tests;

public sealed class EvaluationTests
{
    private static readonly SubmissionInput Input = new("Return 1", 2, DateTimeOffset.UtcNow, null);
    private static IConfiguration Config => new ConfigurationBuilder().AddInMemoryCollection(
        new Dictionary<string, string?> { ["AiAgent:ApiKey"] = "fixture", ["AiAgent:Model"] = "fixture" }).Build();

    [Theory]
    [InlineData(100, 100, 60, 0, 70, "Completed")]
    [InlineData(99, 100, 60, 0, 69.65, "Rejected")]
    [InlineData(70, 70, 70, 70, 70, "Completed")]
    public async Task FullGeminiExchangeUsesServerScores(int logic, int security, int codeQuality, int compliance,
        double expected, string status)
    {
        int turns = 0;
        var http = new FixtureHttp(async request =>
        {
            Assert.Equal("generativelanguage.googleapis.com", request.RequestUri!.Host);
            var body = await request.Content!.ReadFromJsonAsync<JsonObject>();
            object part;
            if (++turns == 1)
            {
                Assert.Equal(4, body!["tools"]![0]!["functionDeclarations"]!.AsArray().Count);
                part = new { functionCall = new { name = "get_submission_code", args = new { }, id = "test" } };
            }
            else if (turns == 2)
            {
                var reply = body!["contents"]![2]!["parts"]![0]!["functionResponse"]!;
                Assert.Equal("test", (string?)reply["id"]);
                Assert.Contains("L1: return 1", (string?)reply["response"]!["result"]);
                Assert.Contains("<STUDENT_SUBMISSION_", (string?)reply["response"]!["result"]);
                part = new { text = "Evidence reviewed" };
            }
            else
            {
                Assert.Null(body!["tools"]);
                Assert.NotNull(body["generationConfig"]!["responseSchema"]);
                part = new { text = System.Text.Json.JsonSerializer.Serialize(new {
                    scores = new { logic, security, codeQuality, compliance }, summary = "مراجعة",
                    strengths = new[] { "وضوح" }, issues = Array.Empty<object>() }) };
            }
            return Json(new { candidates = new[] { new { finishReason = "STOP",
                content = new { role = "model", parts = new[] { part } } } } });
        });
        var reader = new SubmissionReader(http, Config);
        var input = await reader.FromFilesAsync(Input, [Upload("return 1")], default);
        var service = new EvaluationAgentService(new GeminiModelClient(http, Config),
            [new SubmissionCodeTool(), new StaticSecurityScanTool(), new PreviousAttemptTool(new DemoAttempts()), new ComplianceMetadataTool()]);
        var result = await service.EvaluateAsync(input, default);
        Assert.Equal((decimal)expected, result.OverallScore);
        Assert.Equal(status, result.Status);
        Assert.Equal(3, turns);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task GitHubChecksVisibilityAndReadsPinnedBlob(bool isPublic)
    {
        var paths = new List<string>();
        var http = new FixtureHttp(request =>
        {
            Assert.Equal("api.github.com", request.RequestUri!.Host);
            string path = request.RequestUri.AbsolutePath;
            paths.Add(path);
            return Task.FromResult(path switch
            {
                "/repos/team/repo" => Json(new { visibility = isPublic ? "public" : "private" }),
                "/repos/team/repo/git/ref/heads/feature/demo" => Json(new { @ref = "refs/heads/feature/demo", @object = new { sha = "abc" } }),
                "/repos/team/repo/git/commits/abc" => Json(new { tree = new { sha = "tree" } }),
                "/repos/team/repo/git/trees/tree" => Json(new { truncated = false, tree = new[] {
                    new { mode = "100644", path = "bin/old.cs", size = 1, sha = "skip" },
                    new { mode = "100644", path = "src/demo.py", size = 4, sha = "file" } } }),
                "/repos/team/repo/git/blobs/file" => Json(new { content = Convert.ToBase64String("pass"u8.ToArray()) }),
                _ => throw new InvalidOperationException(path)
            });
        });
        var reader = new SubmissionReader(http, Config);
        var read = reader.FromGitHubAsync(Input, "https://github.com/team/repo.git", "feature/demo", default);
        if (isPublic)
        {
            var result = await read;
            Assert.Equal("File src/demo.py\nL1: pass\n", result.NumberedCode);
            Assert.False(result.Truncated);
        }
        else
        {
            Assert.Equal(400, (await Assert.ThrowsAsync<EvaluationException>(() => read)).Status);
            Assert.Single(paths);
        }
    }

    [Fact]
    public async Task UploadBudgetAlsoBoundsStaticReview()
    {
        var reader = new SubmissionReader(new FixtureHttp(_ => throw new InvalidOperationException()), Config);
        var result = await reader.FromFilesAsync(Input, [Upload(new string('a', 26_000) + "secret")], default);
        Assert.True(result.Truncated);
        Assert.Equal(25_000, result.NumberedCode.Length);
        Assert.DoesNotContain("secret", result.Files.Single().Text);
    }

    private static FormFile Upload(string text)
    {
        var stream = new MemoryStream(Encoding.UTF8.GetBytes(text));
        return new FormFile(stream, 0, stream.Length, "Files", "demo.py");
    }
    private static HttpResponseMessage Json(object value) =>
        new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };

    private sealed class FixtureHttp(Func<HttpRequestMessage, Task<HttpResponseMessage>> reply) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new Handler(reply));
        private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => respond(request);
        }
    }
}
