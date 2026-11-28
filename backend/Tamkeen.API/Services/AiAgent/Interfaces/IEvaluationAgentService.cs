using Tamkeen.API.Services.AiAgent.Models;
namespace Tamkeen.API.Services.AiAgent.Interfaces;
public interface IEvaluationAgentService
{
    Task<EvaluationResultDto> EvaluateAsync(Submission submission, CancellationToken ct);
}
