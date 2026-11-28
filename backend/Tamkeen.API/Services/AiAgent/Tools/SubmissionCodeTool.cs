using Tamkeen.API.Services.AiAgent.Interfaces;
using Tamkeen.API.Services.AiAgent.Models;
namespace Tamkeen.API.Services.AiAgent.Tools;
public sealed class SubmissionCodeTool : IAgentTool
{
    public string Name => "get_submission_code";
    public string Description => "Get the numbered text of the current submission.";
    public object Read(Submission submission) => submission.NumberedCode;
}
