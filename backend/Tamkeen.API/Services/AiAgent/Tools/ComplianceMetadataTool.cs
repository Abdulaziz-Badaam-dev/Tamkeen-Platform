using Tamkeen.API.Services.AiAgent.Interfaces;
using Tamkeen.API.Services.AiAgent.Models;
namespace Tamkeen.API.Services.AiAgent.Tools;
public sealed class ComplianceMetadataTool : IAgentTool
{
    public string Name => "get_compliance_metadata";
    public string Description => "Get reviewed file count, extensions, character count, coverage and timing.";
    public object Read(Submission s) => new
    {
        fileCount = s.CollectedFileCount,
        extensions = s.CollectedExtensions,
        totalCharacters = s.CollectedCharacters,
        reviewedFileCount = s.Files.Count,
        reviewedCharacters = s.Files.Sum(f => f.Text.Length),
        truncated = s.Truncated,
        onTime = s.Due is null ? (bool?)null : s.Received <= s.Due
    };
}
