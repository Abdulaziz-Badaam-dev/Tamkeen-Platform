using System.Text.Json.Nodes;
namespace Tamkeen.API.Services.AiAgent.Interfaces;
public interface IAiModelClient
{
    Task<JsonObject> GenerateAsync(JsonObject request, CancellationToken ct);
}
