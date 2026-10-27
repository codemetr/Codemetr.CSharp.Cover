namespace Codemetr.CSharp.Cover;

/// <summary>Missing lines and branches to reach the thresholds.</summary>
public sealed record Gap(int Lines, int Branches);

/// <summary>One place below the worklist thresholds.</summary>
public sealed record WorklistItem
{
    /// <summary>member, type or file.</summary>
    public required string Level { get; init; }
    /// <summary>Full type name (member and type level).</summary>
    public string? Type { get; init; }
    /// <summary>Member with parameter types (member level).</summary>
    public string? Member { get; init; }
    /// <summary>Root-relative file.</summary>
    public required string File { get; init; }
    /// <summary>1-based line of the declaration.</summary>
    public int? Line { get; init; }
    /// <summary>Line coverage in percent; null when nothing is coverable.</summary>
    public double? LineCoverage { get; init; }
    /// <summary>Branch coverage in percent; null without branches.</summary>
    public double? BranchCoverage { get; init; }
    /// <summary>Coverable lines.</summary>
    public int Coverable { get; init; }
    /// <summary>Branch outcomes.</summary>
    public int Branches { get; init; }
    /// <summary>Cyclomatic complexity (member level).</summary>
    public int? Complexity { get; init; }
    /// <summary>CRAP score (member level).</summary>
    public double? Crap { get; init; }
    /// <summary>Which thresholds are missed: line and/or branch.</summary>
    public required IReadOnlyList<string> Below { get; init; }
    /// <summary>Lines and branches still to cover.</summary>
    public required Gap Gap { get; init; }
    /// <summary>False when the coverage data had no line of this code (counted as 0 %).</summary>
    public bool Reported { get; init; } = true;
    /// <summary>Gate status of this member for information: new, worsened, baselined or none.</summary>
    public string Gate { get; init; } = "none";
    /// <summary>Uncovered lines as ranges (member level, capped).</summary>
    public IReadOnlyList<int[]>? UncoveredLines { get; init; }
    /// <summary>Test file that most likely belongs to this code.</summary>
    public string? TestIn { get; init; }
}

/// <summary>Every member, type or file below given thresholds, independent of baseline and gate.</summary>
public static class Worklist
{
    /// <summary>Sort orders.</summary>
    public static readonly IReadOnlyList<string> Sorts = ["gap", "crap", "path"];
    /// <summary>Levels.</summary>
    public static readonly IReadOnlyList<string> Levels = ["member", "type", "file"];

    /// <summary>Builds the complete, sorted list.</summary>
    public static IReadOnlyList<WorklistItem> Build(CoverageModel model, double line, double? branch, string level, string sort,
        IReadOnlyDictionary<string, string>? gateStatus = null)
    {
        IEnumerable<WorklistItem> items = level switch
        {
            "member" => model.Members.Select(m => Item(new Place("member", m.Member.Type, m.Member.Name, m.Member.File, m.Member.Line), m.Count, line, branch) is { } item
                ? item with
                {
                    Complexity = m.Member.Complexity, Crap = m.Crap, Reported = m.Reported,
                    Gate = gateStatus?.GetValueOrDefault(m.Member.Key) ?? "none",
                    UncoveredLines = Finding.Ranges(m.UncoveredLines).Take(Finding.MaxRanges).ToArray(),
                } : null).OfType<WorklistItem>(),
            "type" => model.Types.Select(t => Item(new Place("type", t.Type.FullName, null, t.Type.File, t.Type.Line), t.Count, line, branch)).OfType<WorklistItem>(),
            "file" => model.Files.Select(f => Item(new Place("file", null, null, f.File.Path, null), f.Count, line, branch) is { } item
                ? item with { Reported = f.Reported } : null).OfType<WorklistItem>(),
            _ => throw new ArgumentException($"Unknown worklist level '{level}'"),
        };
        return sort switch
        {
            "gap" => items.OrderByDescending(i => i.Gap.Lines + i.Gap.Branches).ThenByDescending(i => i.Crap ?? 0).ThenBy(i => i.File, StringComparer.Ordinal)
                .ThenBy(i => i.Line ?? 0).ThenBy(i => i.Member, StringComparer.Ordinal).ToArray(),
            "crap" => items.OrderByDescending(i => i.Crap ?? 0).ThenByDescending(i => i.Gap.Lines + i.Gap.Branches).ThenBy(i => i.File, StringComparer.Ordinal)
                .ThenBy(i => i.Line ?? 0).ThenBy(i => i.Member, StringComparer.Ordinal).ToArray(),
            "path" => items.OrderBy(i => i.File, StringComparer.Ordinal).ThenBy(i => i.Line ?? 0).ThenBy(i => i.Member, StringComparer.Ordinal).ToArray(),
            _ => throw new ArgumentException($"Unknown worklist sort '{sort}'"),
        };
    }

    sealed record Place(string Level, string? Type, string? Member, string File, int? Line);

    static WorklistItem? Item(Place place, CoverageCount count, double minLine, double? minBranch)
    {
        var below = new List<string>();
        if (count.LineRatio is { } lr && lr * 100 < minLine - 1e-9) below.Add("line");
        if (minBranch is { } mb && count.BranchRatio is { } br && br * 100 < mb - 1e-9) below.Add("branch");
        if (below.Count == 0) return null;
        return new WorklistItem
        {
            Level = place.Level, Type = place.Type, Member = place.Member, File = place.File, Line = place.Line,
            LineCoverage = count.LinePercent, BranchCoverage = count.BranchPercent, Coverable = count.CoverableLines, Branches = count.Branches,
            Below = below,
            Gap = new Gap(Missing(count.CoveredLines, count.CoverableLines, minLine), minBranch is { } b ? Missing(count.CoveredBranches, count.Branches, b) : 0),
        };
    }

    // Lines (or branches) still to cover so that covered / total reaches the threshold.
    static int Missing(int covered, int total, double threshold) => Math.Max(0, (int)Math.Ceiling(total * threshold / 100 - 1e-9) - covered);
}
