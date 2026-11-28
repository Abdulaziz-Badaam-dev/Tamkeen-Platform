using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Tamkeen.API.Services.AiAgent.Interfaces;
using Tamkeen.API.Services.AiAgent.Models;
using Tamkeen.API.Services.AiAgent.Prompts;

namespace Tamkeen.API.Services.AiAgent;

public sealed class EvaluationAgentService(IAiModelClient model, IEnumerable<IAgentTool> tools) : IEvaluationAgentService
{
    private static readonly JsonSerializerOptions ReportOptions = new(JsonSerializerDefaults.Web)
    {
        RespectRequiredConstructorParameters = true, RespectNullableAnnotations = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        NumberHandling = JsonNumberHandling.Strict
    };

    public async Task<EvaluationResultDto> EvaluateAsync(Submission submission, CancellationToken ct)
    {
        var tag = "STUDENT_SUBMISSION_" + Convert.ToHexString(RandomNumberGenerator.GetBytes(4));
        var evidence = tools.ToDictionary(tool => tool.Name, tool => new Lazy<string>(() =>
        {
            var value = tool.Read(submission);
            var text = value as string ?? JsonSerializer.Serialize(value);
            return $"<{tag}>\n{text}\n</{tag}>";
        }));
        var declarations = tools.Select(tool => new { name = tool.Name, description = tool.Description }).ToArray();
        string assignment = "Assignment input: " + JsonSerializer.Serialize(submission.Task);
        var history = new JsonArray(EvaluationPromptBuilder.Message("user", assignment +
            "\nInspect submission evidence with the supplied tools. Request tools together when possible."));
        string reviewNotes = "";
        for (int round = 0; round < 4; round++)
        {
            var request = EvaluationPromptBuilder.Request(history);
            request["tools"] = JsonSerializer.SerializeToNode(new[] { new { functionDeclarations = declarations } });
            var answer = await model.GenerateAsync(request, ct);
            var calls = answer["parts"]!.AsArray().Where(p => p?["functionCall"] != null)
                .Select(p => p!["functionCall"]!).ToArray();
            if (calls.Length == 0)
            {
                var notes = string.Concat(answer["parts"]!.AsArray()
                    .Where(p => p?["thought"]?.GetValue<bool>() != true).Select(p => p?["text"]?.GetValue<string>()));
                reviewNotes = notes[..Math.Min(notes.Length, 2000)];
                break;
            }
            if (calls.Length > declarations.Length) throw InvalidReport();
            var replies = new JsonArray();
            foreach (var call in calls)
            {
                var name = call["name"]?.GetValue<string>() ?? "";
                if (!evidence.TryGetValue(name, out var data) || call["args"] is not (null or JsonObject { Count: 0 }))
                    throw InvalidReport();
                var reply = new JsonObject { ["name"] = name, ["response"] = new JsonObject { ["result"] = data.Value } };
                if (call["id"] is { } id) reply["id"] = id.DeepClone();
                replies.Add(new JsonObject { ["functionResponse"] = reply });
            }
            // Preserve the full model turn, including any opaque Gemini thought signatures.
            history.Add(answer.DeepClone());
            history.Add(new JsonObject { ["role"] = "user", ["parts"] = replies });
        }

        // Provide every bounded evidence source even when the model skipped a tool.
        var finalText = assignment + "\nReturn the structured evaluation using this evidence:\n" +
            string.Join("\n", evidence.Select(entry => entry.Key + "\n" + entry.Value.Value)) +
            $"\nPreliminary review notes (verify against evidence): <{tag}>{reviewNotes}</{tag}>";
        var final = EvaluationPromptBuilder.Request(new JsonArray(EvaluationPromptBuilder.Message("user", finalText)));
        final["generationConfig"] = new JsonObject
        {
            ["responseMimeType"] = "application/json", ["responseSchema"] = EvaluationPromptBuilder.Schema()
        };
        var result = await model.GenerateAsync(final, ct);
        var json = string.Concat(result["parts"]!.AsArray().Where(p => p?["thought"]?.GetValue<bool>() != true)
            .Select(p => p?["text"]?.GetValue<string>()));
        var report = JsonSerializer.Deserialize<ModelReport>(json, ReportOptions) ?? throw InvalidReport();
        var s = report.Scores;
        if (new[] { s.Logic, s.Security, s.CodeQuality, s.Compliance }.Any(v => v is < 0 or > 100) ||
            string.IsNullOrWhiteSpace(report.Summary) || report.Strengths.Any(string.IsNullOrWhiteSpace) ||
            report.Issues.Any(i => i is null || !ValidIssue(i, submission))) throw InvalidReport();
        decimal total = Math.Round(.35m * s.Logic + .20m * s.Security + .25m * s.CodeQuality + .20m * s.Compliance,
            2, MidpointRounding.AwayFromZero);
        return new(total, total >= 70 ? "Completed" : "Rejected", s, report.Summary, report.Strengths, report.Issues);
    }

    private static bool ValidIssue(Issue issue, Submission submission)
    {
        var file = submission.Files.FirstOrDefault(f => f.Name == issue.File);
        return issue.Criterion is ("logic" or "security" or "codeQuality" or "compliance") &&
            issue.Severity is ("low" or "medium" or "high" or "critical") &&
            !string.IsNullOrWhiteSpace(issue.Explanation) && !string.IsNullOrWhiteSpace(issue.Suggestion) &&
            (issue.File is null || file != null) &&
            (issue.Line is null || (file != null && issue.Line >= 1 && issue.Line <= 1 + file.Text.Count(c => c == '\n')));
    }

    private static EvaluationException InvalidReport() => new(502, "Invalid Gemini evaluation response.");
}
