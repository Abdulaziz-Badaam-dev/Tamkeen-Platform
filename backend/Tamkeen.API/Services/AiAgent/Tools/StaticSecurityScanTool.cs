using System.Text.RegularExpressions;
using Tamkeen.API.Services.AiAgent.Interfaces;
using Tamkeen.API.Services.AiAgent.Models;
namespace Tamkeen.API.Services.AiAgent.Tools;
public sealed class StaticSecurityScanTool : IAgentTool
{
    public string Name => "run_static_security_scan";
    public string Description => "Get heuristic review leads with file and line references.";
    private static readonly (string Label, Regex Pattern)[] Rules =
    [
        Rule("SQL construction", """(select|insert|update|delete).*(["']\s*\+|\$\{|\{[a-z_])"""),
        Rule("Possible embedded credential", """(password|api[_-]?key|secret|token)\s*[:=]\s*["'][^"']+["']"""),
        Rule("Weak hash", @"\b(md5|sha1)\b"),
        Rule("TLS verification disabled", @"(rejectUnauthorized\s*:\s*false|verify\s*=\s*false|DangerousAcceptAnyServerCertificateValidator|ServerCertificateCustomValidationCallback.*=>\s*true)"),
        Rule("Check input validation at this boundary", @"(Request\.(Query|Form)|req\.(body|params|query)|request\.(args|form))\b")
    ];
    private static (string, Regex) Rule(string label, string pattern) =>
        (label, new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking));

    public object Read(Submission submission)
    {
        var findings = new List<object>();
        foreach (var file in submission.Files)
        {
            using var reader = new StringReader(file.Text);
            for (int line = 1; reader.ReadLine() is { } text; line++)
                foreach (var rule in Rules)
                    if (rule.Pattern.IsMatch(text))
                    {
                        findings.Add(new { file = file.Name, line, hint = rule.Label });
                        if (findings.Count == 50) return findings;
                    }
        }
        return findings;
    }
}
