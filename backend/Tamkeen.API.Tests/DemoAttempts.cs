using Tamkeen.API.Services.AiAgent.Models;
using Tamkeen.API.Services.AiAgent.Tools;

namespace Tamkeen.API.Tests;

internal sealed class DemoAttempts : IPreviousAttempts
{
    public IReadOnlyList<Issue> LatestRejected(Submission submission) => [];
}
