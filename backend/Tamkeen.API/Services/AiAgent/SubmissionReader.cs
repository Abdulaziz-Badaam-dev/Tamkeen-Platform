using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Tamkeen.API.Services.AiAgent.Models;

namespace Tamkeen.API.Services.AiAgent;

public sealed partial class SubmissionReader(IHttpClientFactory clients, IConfiguration config)
{
    public const int MaxBytes = 2 * 1024 * 1024;
    private const int CharacterBudget = 25_000;
    private const int MaxRepositoryFiles = 40;
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
        { ".cs", ".js", ".ts", ".py", ".sql", ".json", ".txt" };
    private static readonly HashSet<string> Excluded = new(StringComparer.OrdinalIgnoreCase)
        { "bin", "obj", "node_modules", ".git" };

    public async Task<Submission> FromFilesAsync(SubmissionInput input, IReadOnlyList<IFormFile> files, CancellationToken ct)
    {
        if (files.Count is < 1 or > 5 || files.Sum(f => f.Length) > MaxBytes)
            throw Invalid("Upload one to five files, up to 2 MiB in total.");
        var sources = new List<SourceFile>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files)
        {
            var name = Path.GetFileName(file.FileName.Replace('\\', '/'));
            if (!Extensions.Contains(Path.GetExtension(name)) || name.Length > 255 || name.Any(char.IsControl) || !names.Add(name))
                throw Invalid("Unsupported, ambiguous or duplicate file name.");
            using var stream = new MemoryStream();
            await file.CopyToAsync(stream, ct);
            sources.Add(new(name, Decode(stream.ToArray())));
        }
        return Prepare(input, sources, false);
    }

    public async Task<Submission> FromGitHubAsync(SubmissionInput input, string url, string branch, CancellationToken ct)
    {
        var match = RepositoryUrl().Match(url);
        if (!match.Success || match.Groups["repo"].Value is "." or ".." || string.IsNullOrWhiteSpace(branch)
            || branch.Length > 255 || branch.Any(char.IsControl) || branch.Split('/').Any(p => p is "" or "." or ".."))
            throw Invalid("Supply a public HTTPS GitHub repository URL and a branch name.");
        string root = $"https://api.github.com/repos/{match.Groups["owner"].Value}/{match.Groups["repo"].Value}";
        using var client = clients.CreateClient("github");
        var token = config["GitHub:PersonalAccessToken"];
        if (!string.IsNullOrWhiteSpace(token)) client.DefaultRequestHeaders.Authorization = new("Bearer", token);
        var repo = await GetJsonAsync(client, root, ct);
        if (!repo.TryGetProperty("visibility", out var visibility) || visibility.GetString() != "public")
            throw Invalid("Only public repositories are supported.");
        var branchPath = string.Join("/", branch.Split('/').Select(Uri.EscapeDataString));
        var reference = await GetJsonAsync(client, $"{root}/git/ref/heads/{branchPath}", ct);
        if (reference.GetProperty("ref").GetString() != $"refs/heads/{branch}") throw Invalid("Unknown branch.");
        var commit = await GetJsonAsync(client, $"{root}/git/commits/{reference.GetProperty("object").GetProperty("sha").GetString()}", ct);
        var tree = await GetJsonAsync(client, $"{root}/git/trees/{commit.GetProperty("tree").GetProperty("sha").GetString()}?recursive=1", ct);
        bool truncated = tree.GetProperty("truncated").GetBoolean();
        var files = new List<SourceFile>();
        int bytes = 0, characters = 0;
        foreach (var item in tree.GetProperty("tree").EnumerateArray())
        {
            var name = item.GetProperty("path").GetString()!;
            if (item.GetProperty("mode").GetString() is not ("100644" or "100755") ||
                !Extensions.Contains(Path.GetExtension(name)) || name.Split('/').Any(Excluded.Contains)) continue;
            if (name.Any(char.IsControl) || name.Length > 1024) { truncated = true; continue; }
            if (files.Count >= MaxRepositoryFiles || characters >= CharacterBudget) { truncated = true; break; }
            long size = item.GetProperty("size").GetInt64();
            if (size < 0 || size > MaxBytes - bytes) { truncated = true; continue; }
            var blob = await GetJsonAsync(client, $"{root}/git/blobs/{item.GetProperty("sha").GetString()}", ct);
            var data = Convert.FromBase64String(blob.GetProperty("content").GetString()!);
            if (data.Length != size) throw new EvaluationException(502, "Inconsistent GitHub blob.");
            var source = Decode(data);
            files.Add(new(name, source));
            bytes += data.Length;
            characters += source.Length;
        }
        return Prepare(input, files, truncated);
    }

    private static async Task<JsonElement> GetJsonAsync(HttpClient client, string url, CancellationToken ct)
    {
        using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) throw Invalid("Repository or branch unavailable.");
        if (!response.IsSuccessStatusCode) throw new EvaluationException(502, $"GitHub returned HTTP {(int)response.StatusCode}.");
        await response.Content.LoadIntoBufferAsync(4 * 1024 * 1024, ct);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(ct));
        return doc.RootElement.Clone();
    }

    private static string Decode(byte[] bytes)
    {
        try
        {
            var text = Utf8.GetString(bytes).TrimStart('\uFEFF');
            return text.Contains('\0') ? throw Invalid("Binary files are unsupported.") : text;
        }
        catch (DecoderFallbackException) { throw Invalid("Expected UTF-8 text."); }
    }

    private static Submission Prepare(SubmissionInput input, List<SourceFile> files, bool truncated)
    {
        var numbered = new StringBuilder();
        var reviewed = new List<SourceFile>();
        foreach (var file in files)
        {
            string heading = $"File {file.Name}\n";
            if (heading.Length + numbered.Length >= CharacterBudget) { truncated = true; break; }
            numbered.Append(heading);
            using var reader = new StringReader(file.Text);
            var visible = new List<string>();
            for (int line = 1; reader.ReadLine() is { } content; line++)
            {
                var prefix = $"L{line}: ";
                int room = CharacterBudget - numbered.Length - prefix.Length - 1;
                if (room < 0) { truncated = true; break; }
                bool cut = content.Length > room;
                if (cut)
                {
                    if (room > 0 && char.IsHighSurrogate(content[room - 1])) room--;
                    content = content[..room];
                }
                numbered.Append(prefix).Append(content).Append('\n');
                visible.Add(content);
                if (cut) { truncated = true; break; }
            }
            reviewed.Add(new(file.Name, string.Join('\n', visible)));
        }
        if (!reviewed.Any(f => !string.IsNullOrWhiteSpace(f.Text))) throw Invalid("No reviewable source text.");
        return new(input.Task, input.Attempt, input.Received, input.Due, reviewed, numbered.ToString(), truncated,
            files.Count, files.Sum(f => f.Text.Length),
            files.Select(f => Path.GetExtension(f.Name).ToLowerInvariant()).Distinct().ToArray());
    }

    private static EvaluationException Invalid(string reason) => new(400, reason);

    [GeneratedRegex(@"\Ahttps://github\.com/(?<owner>[A-Za-z0-9-]+)/(?<repo>[A-Za-z0-9_.-]+?)(?:\.git)?/?\z", RegexOptions.IgnoreCase)]
    private static partial Regex RepositoryUrl();
}
public sealed record SubmissionInput(string Task, int Attempt, DateTimeOffset Received, DateTimeOffset? Due);
