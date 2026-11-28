using System.Net;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Tamkeen.API.Services.AiAgent.Interfaces;
using Xunit;

namespace Tamkeen.API.Tests;

public sealed partial class BackendCoreTests
{
    [Fact]
    public async Task SimultaneousPassingSubmissionsCommitOneReportAndOneUnlock()
    {
        using var factory = new CoreFactory();
        using var app = factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.AddSingleton<IAiModelClient>(new ConcurrentModel(factory.Model))));
        using var client = app.CreateClient();
        await Register(client, "parallel@example.test");
        int track = (int)(await Read(await client.GetAsync("/api/tracks")))[0]!["id"]!;
        await Read(await client.PostAsync($"/api/tracks/{track}/enroll", null));
        int task = (int)(await Progress(client, track))["tasks"]![0]!["taskId"]!;
        var responses = await Task.WhenAll(Submit(client, task, "pass-one"), Submit(client, task, "pass-one"));
        Assert.Equal(new[] { HttpStatusCode.OK, HttpStatusCode.Conflict }, responses.Select(r => r.StatusCode).Order());
        var tasks = (await Progress(client, track))["tasks"]!.AsArray();
        Assert.Equal(1, (int)tasks[0]!["attemptCount"]!);
        Assert.Equal("Active", Text(tasks[1]!, "status"));
        Assert.Single((await Read(await client.GetAsync($"/api/tasks/{task}")))["submissions"]!.AsArray());
    }

    private sealed class ConcurrentModel(IAiModelClient inner) : IAiModelClient
    {
        private readonly TaskCompletionSource bothReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int pending;

        public async Task<JsonObject> GenerateAsync(JsonObject request, CancellationToken ct)
        {
            if (request["generationConfig"] != null)
            {
                if (Interlocked.Increment(ref pending) == 2) bothReady.TrySetResult();
                await bothReady.Task.WaitAsync(TimeSpan.FromSeconds(10), ct);
            }
            return await inner.GenerateAsync(request, ct);
        }
    }
}
