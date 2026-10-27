using System.Globalization;
using System.Text.RegularExpressions;

namespace Codemetr.CSharp.Cover;

/// <summary>Reads the added and changed lines (new side) of a unified diff, such as <c>git diff</c> output.</summary>
public static partial class UnifiedDiff
{
    /// <summary>Changed lines per file path as the diff names it (prefixes <c>a/</c> and <c>b/</c> removed).</summary>
    public static Dictionary<string, SortedSet<int>> Parse(string diff)
    {
        var result = new Dictionary<string, SortedSet<int>>(StringComparer.Ordinal);
        SortedSet<int>? current = null;
        int line = 0, oldLeft = 0, newLeft = 0;
        foreach (var raw in diff.Split('\n'))
        {
            var text = raw.TrimEnd('\r');
            // Inside a hunk the header counts say how many lines follow, so "+++" or "---" content is not a file header.
            if (oldLeft > 0 || newLeft > 0)
            {
                if (text.Length == 0) { oldLeft--; newLeft--; line++; continue; }
                switch (text[0])
                {
                    case '+': current?.Add(line); line++; newLeft--; break;
                    case '-': oldLeft--; break;
                    case ' ': line++; oldLeft--; newLeft--; break;
                    case '\\': break;
                    default: oldLeft = newLeft = 0; break;
                }
                continue;
            }
            if (text.StartsWith("+++ ", StringComparison.Ordinal))
            {
                var path = PathOf(text[4..]);
                current = path is null ? null : result.TryGetValue(path, out var existing) ? existing : result[path] = [];
                continue;
            }
            var hunk = HunkPattern().Match(text);
            if (!hunk.Success) continue;
            oldLeft = Count(hunk.Groups[1]);
            line = int.Parse(hunk.Groups[2].Value, CultureInfo.InvariantCulture);
            newLeft = Count(hunk.Groups[3]);
        }
        return result;
    }

    static int Count(Group group) => group.Success ? int.Parse(group.Value, CultureInfo.InvariantCulture) : 1;

    // "b/src/A.cs", "/dev/null", "\"b/src/with space.cs\"", or a path followed by a tab and a timestamp.
    static string? PathOf(string value)
    {
        var path = value.Split('\t')[0].Trim();
        if (path.Length > 1 && path[0] == '"' && path[^1] == '"')
        {
            if (path.Contains('\\')) throw new InvalidDataException($"Quoted path in diff: {path}; create the diff with 'git -c core.quotePath=false diff'");
            path = path[1..^1];
        }
        if (path == "/dev/null") return null;
        return path.StartsWith("b/", StringComparison.Ordinal) ? path[2..] : path;
    }

    [GeneratedRegex(@"^@@ -\d+(?:,(\d+))? \+(\d+)(?:,(\d+))? @@")]
    private static partial Regex HunkPattern();
}
