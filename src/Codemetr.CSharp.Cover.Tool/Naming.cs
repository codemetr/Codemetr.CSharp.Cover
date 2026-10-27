using System.Text.RegularExpressions;
using Codemetr.CSharp;

namespace Codemetr.CSharp.Cover.Tool;

/// <summary>Type name matching as in codemetr-csharp: simple name, full name or full-name suffix; type parameters may be omitted.</summary>
internal static class TypeName
{
    public static string Simple(string query)
    {
        var plain = WithoutTypeParameters(query);
        return plain[(plain.LastIndexOf('.') + 1)..];
    }

    public static bool Matches(string fullName, string query)
    {
        foreach (var candidate in new[] { fullName, WithoutTypeParameters(fullName) })
            if (candidate == query || candidate.EndsWith("." + query, StringComparison.Ordinal)) return true;
        return Simple(fullName) == query;
    }

    static string WithoutTypeParameters(string name)
    {
        var result = new System.Text.StringBuilder(name.Length);
        var depth = 0;
        foreach (var character in name)
        {
            if (character == '<') depth++;
            else if (character == '>') depth--;
            else if (depth == 0) result.Append(character);
        }
        return result.ToString();
    }
}

/// <summary>Finds type declarations in code that is never measured: build output, generated files, excluded paths.</summary>
internal sealed class UnmeasuredDeclarations(string root, IReadOnlyCollection<string> exclude)
{
    List<(string Path, string Text)>? files;
    readonly Dictionary<string, bool> cache = new(StringComparer.Ordinal);

    public bool Contains(string fullType)
    {
        if (cache.TryGetValue(fullType, out var known)) return known;
        files ??= SourceCollector.EveryCSharpFile(root)
            .Where(f => new FileInfo(f).Length <= SourceCollector.MaximumFileBytes)
            .Select(f => (Path: TestProjects.Relative(root, f), Text: File.ReadAllText(f)))
            .Where(f => SourceCollector.IsInSkippedDirectory(f.Path) || SourceCollector.IsNeverMeasured(new SourceDocument(f.Path, f.Text), exclude))
            .ToList();
        var pattern = new Regex(@"\b(class|struct|record|interface)\s+" + Regex.Escape(TypeName.Simple(fullType)) + @"\b", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        return cache[fullType] = files.Any(f => pattern.IsMatch(f.Text));
    }
}
