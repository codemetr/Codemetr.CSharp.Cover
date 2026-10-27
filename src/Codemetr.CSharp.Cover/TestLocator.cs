using System.Text.RegularExpressions;

namespace Codemetr.CSharp.Cover;

/// <summary>
/// Suggests the test file for a type: a test file named after the type (OrderServiceTests.cs) first, else the
/// test file that mentions the type most often. A hint for where a new test belongs, not a guarantee.
/// </summary>
public sealed class TestLocator
{
    readonly IReadOnlyList<(string Path, string Name, string Text)> files;
    readonly Dictionary<string, string?> cache = new(StringComparer.Ordinal);

    /// <summary>Indexes the C# files of the given test project directories (root-relative paths).</summary>
    public TestLocator(string root, IEnumerable<string> testDirectories)
    {
        var list = new List<(string, string, string)>();
        foreach (var directory in testDirectories.Distinct(StringComparer.Ordinal))
        {
            var full = Path.GetFullPath(directory, root);
            if (!Directory.Exists(full)) continue;
            foreach (var file in Codemetr.CSharp.SourceCollector.EveryCSharpFile(full))
            {
                var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
                if (Codemetr.CSharp.SourceCollector.IsInSkippedDirectory(relative)) continue;
                var info = new FileInfo(file);
                if (info.Length > Codemetr.CSharp.SourceCollector.MaximumFileBytes) continue;
                list.Add((relative, Path.GetFileNameWithoutExtension(file), File.ReadAllText(file)));
            }
        }
        files = list.OrderBy(f => f.Item1, StringComparer.Ordinal).ToArray();
    }

    /// <summary>The suggested test file for a full type name, or null.</summary>
    public string? For(string fullType)
    {
        if (cache.TryGetValue(fullType, out var known)) return known;
        var simple = CoverConfig.StripGenerics(fullType);
        simple = simple[(simple.LastIndexOf('.') + 1)..];
        string? result = null;
        if (simple.Length > 0)
        {
            result = files.FirstOrDefault(f => f.Name == simple + "Tests" || f.Name == simple + "Test" || f.Name == simple + "Specs").Path;
            if (result is null)
            {
                var pattern = new Regex(@"\b" + Regex.Escape(simple) + @"\b", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
                result = files.Select(f => (f.Path, Count: pattern.Matches(f.Text).Count)).Where(f => f.Count > 0)
                    .OrderByDescending(f => f.Count).ThenBy(f => f.Path, StringComparer.Ordinal).Select(f => f.Path).FirstOrDefault();
            }
        }
        cache[fullType] = result;
        return result;
    }
}
