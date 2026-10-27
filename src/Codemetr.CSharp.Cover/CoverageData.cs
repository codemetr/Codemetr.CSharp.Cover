namespace Codemetr.CSharp.Cover;

/// <summary>Line coverage per source file, keyed by a path (absolute when read, root-relative after <see cref="PathMapper"/>).</summary>
public sealed class CoverageData
{
    readonly Dictionary<string, SortedDictionary<int, LineHit>> files;

    /// <summary>Creates an empty set; <paramref name="comparer"/> decides whether paths differ by case.</summary>
    public CoverageData(StringComparer? comparer = null) => files = new(comparer ?? StringComparer.Ordinal);

    /// <summary>Files with coverage data.</summary>
    public IReadOnlyCollection<string> Files => files.Keys;

    /// <summary>Lines of one file, or null when the file has no coverage data.</summary>
    public IReadOnlyDictionary<int, LineHit>? Lines(string file) => files.GetValueOrDefault(file);

    /// <summary>True when the file has coverage data.</summary>
    public bool Contains(string file) => files.ContainsKey(file);

    /// <summary>Adds a measurement, merging it with an earlier one of the same line.</summary>
    public void Add(string file, int line, LineHit hit)
    {
        if (!files.TryGetValue(file, out var lines)) files[file] = lines = new SortedDictionary<int, LineHit>();
        lines[line] = lines.TryGetValue(line, out var previous) ? previous.Merge(hit) : hit;
    }

    /// <summary>Adds every line of <paramref name="other"/>; a line is covered when any input covers it.</summary>
    public void Merge(CoverageData other)
    {
        foreach (var file in other.files)
            foreach (var line in file.Value) Add(file.Key, line.Key, line.Value);
    }
}
