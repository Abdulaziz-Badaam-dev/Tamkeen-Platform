using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Tamkeen.API.Services.AiAgent.Interfaces;
using Tamkeen.API.Services.AiAgent.Models;

namespace Tamkeen.API.Services.AiAgent.Clients;
public sealed class GeminiModelClient(IHttpClientFactory clients, IConfiguration config) : IAiModelClient
{
    public async Task<JsonObject> GenerateAsync(JsonObject request, CancellationToken ct)
    {
        var key = config["AiAgent:ApiKey"];
        var model = config["AiAgent:Model"];
        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(model))
            throw new EvaluationException(503, "Configure AiAgent:ApiKey and AiAgent:Model.");
        if (!Regex.IsMatch(model, @"\A[a-zA-Z0-9_.-]+\z") ||
            !Uri.TryCreate(config["AiAgent:EndpointBaseUrl"] ?? "https://generativelanguage.googleapis.com/",
                UriKind.Absolute, out var endpoint) || endpoint.Scheme != "https" ||
            endpoint.Host != "generativelanguage.googleapis.com" || !endpoint.IsDefaultPort || endpoint.UserInfo.Length != 0)
            throw new EvaluationException(503, "Invalid Gemini configuration.");

        using var client = clients.CreateClient("gemini");
        using var message = new HttpRequestMessage(HttpMethod.Post, new Uri(endpoint, $"/v1beta/models/{model}:generateContent"));
        message.Headers.Add("x-goog-api-key", key);
        message.Content = JsonContent.Create(request);
        using var response = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode)
            throw new EvaluationException(502, $"Gemini returned HTTP {(int)response.StatusCode}.");
        await response.Content.LoadIntoBufferAsync(2 * 1024 * 1024, ct);
        var json = await JsonNode.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        if (json?["candidates"] is not JsonArray { Count: > 0 } candidates ||
            candidates[0]?["finishReason"]?.GetValue<string>() != "STOP" ||
            candidates[0]?["content"] is not JsonObject content || content["parts"] is not JsonArray { Count: > 0 })
            throw new EvaluationException(502, "Gemini returned no complete candidate.");
        return content;
    }
}
