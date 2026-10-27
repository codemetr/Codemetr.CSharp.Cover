namespace Codemetr.CSharp.Cover;

/// <summary>
/// Turns report paths into root-relative paths with '/', so baselines and diffs read the same on every machine.
/// Optional prefix maps translate paths of reports produced elsewhere (a container, another checkout).
/// </summary>
public sealed class PathMapper
{
    readonly string root;
    readonly IReadOnlyList<(string From, string To)> maps;
    readonly StringComparison comparison;

    /// <summary>Creates a mapper for <paramref name="root"/>. Paths compare without case unless the file system is Linux.</summary>
    public PathMapper(string root, IEnumerable<(string From, string To)>? maps = null, bool? ignoreCase = null)
    {
        this.root = Normalize(Path.GetFullPath(root)).TrimEnd('/') + "/";
        this.maps = (maps ?? []).Select(map => (Normalize(map.From), Normalize(map.To))).ToArray();
        comparison = (ignoreCase ?? !OperatingSystem.IsLinux()) ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    }

    /// <summary>Comparer that matches how this mapper compares paths.</summary>
    public StringComparer Comparer => comparison == StringComparison.Ordinal ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;

    /// <summary>The root-relative path, or null when the path lies outside the root.</summary>
    public string? Relative(string reportPath)
    {
        var path = Normalize(reportPath);
        foreach (var (from, to) in maps)
            if (path.StartsWith(from, comparison)) { path = to + path[from.Length..]; break; }
        if (!CoverageReportReader.IsRooted(path)) return path.TrimStart('/');
        return path.StartsWith(root, comparison) ? path[root.Length..] : null;
    }

    /// <summary>Maps every file of <paramref name="data"/>; files outside the root are returned separately.</summary>
    public (CoverageData Mapped, IReadOnlyList<string> Foreign) Map(CoverageData data)
    {
        var mapped = new CoverageData(Comparer);
        var foreign = new List<string>();
        foreach (var file in data.Files)
        {
            if (Relative(file) is not { } relative) { foreign.Add(file); continue; }
            foreach (var (line, hit) in data.Lines(file)!) mapped.Add(relative, line, hit);
        }
        return (mapped, foreign);
    }

    /// <summary>Forward slashes, without "." segments and with ".." resolved.</summary>
    public static string Normalize(string path)
    {
        var parts = new List<string>();
        foreach (var part in path.Replace('\\', '/').Split('/'))
        {
            if (part == "." ) continue;
            if (part == ".." && parts.Count > 0 && parts[^1] is not ("" or "..")) { parts.RemoveAt(parts.Count - 1); continue; }
            parts.Add(part);
        }
        return string.Join('/', parts);
    }
}
