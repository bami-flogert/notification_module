using System.Text.RegularExpressions;

namespace NotificationModule.Tests.Security;

/// <summary>
/// Static analysis guard: Producer and Consumer must not log PII field names
/// inside ILogger calls (see docs/LOGGING.md).
/// </summary>
public sealed class LogRedactionTests
{
    private static readonly string[] ForbiddenSubstrings =
    [
        "PatientName",
        "PatientPhone",
        "PatientEmail",
    ];

    private static readonly Regex LogCallStart = new(
        @"\.Log(Information|Error|Warning|Debug|Trace)\s*\(",
        RegexOptions.Compiled);

    [Fact]
    public void Producer_and_Consumer_log_calls_do_not_reference_forbidden_pii_fields()
    {
        var repoRoot = FindRepoRoot();
        var producerDir = Path.Combine(repoRoot, "src", "NotificationModule.Producer");
        var consumerDir = Path.Combine(repoRoot, "src", "NotificationModule.Consumer");

        var violations = new List<string>();
        foreach (var directory in new[] { producerDir, consumerDir })
        {
            foreach (var file in Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories))
            {
                violations.AddRange(FindViolationsInFile(file));
            }
        }

        Assert.True(
            violations.Count == 0,
            "Log statements must not reference PII fields. See docs/LOGGING.md." + Environment.NewLine
            + string.Join(Environment.NewLine, violations));
    }

    private static IEnumerable<string> FindViolationsInFile(string filePath)
    {
        var lines = File.ReadAllLines(filePath);
        var i = 0;
        while (i < lines.Length)
        {
            if (!LogCallStart.IsMatch(lines[i]))
            {
                i++;
                continue;
            }

            var statement = new List<string> { lines[i] };
            var startLine = i + 1;
            while (i < lines.Length && !lines[i].TrimEnd().EndsWith(");", StringComparison.Ordinal))
            {
                i++;
                if (i < lines.Length)
                    statement.Add(lines[i]);
            }

            var text = string.Join(Environment.NewLine, statement);
            foreach (var forbidden in ForbiddenSubstrings)
            {
                if (text.Contains(forbidden, StringComparison.Ordinal))
                {
                    var relative = Path.GetRelativePath(FindRepoRoot(), filePath);
                    yield return $"{relative}:{startLine}: references '{forbidden}' in log call";
                }
            }

            i++;
        }
    }

    private static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir, "NotificationModule.sln")))
                return dir;

            dir = Directory.GetParent(dir)?.FullName;
        }

        throw new InvalidOperationException("Could not locate repository root (NotificationModule.sln).");
    }
}
