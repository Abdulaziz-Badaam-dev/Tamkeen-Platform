using Tamkeen.API.Services.AiAgent.Models;
namespace Tamkeen.API.Services.AiAgent.Interfaces;
public interface IAgentTool
{
    string Name { get; }
    string Description { get; }
    object Read(Submission submission);
}
