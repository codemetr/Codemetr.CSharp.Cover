namespace Codemetr.CSharp.Cover;

/// <summary>Covered and coverable lines and branches of any scope.</summary>
public sealed record CoverageCount(int CoveredLines, int CoverableLines, int CoveredBranches, int Branches)
{
    /// <summary>Nothing coverable.</summary>
    public static CoverageCount Zero { get; } = new(0, 0, 0, 0);

    /// <summary>Exact line ratio 0..1, or null when nothing is coverable.</summary>
    public double? LineRatio => CoverableLines == 0 ? null : (double)CoveredLines / CoverableLines;
    /// <summary>Exact branch ratio 0..1, or null when there are no branches (never reported as 100 %).</summary>
    public double? BranchRatio => Branches == 0 ? null : (double)CoveredBranches / Branches;
    /// <summary>Line coverage in percent, rounded down to one decimal so a failing value never displays as passing.</summary>
    public double? LinePercent => Percent(LineRatio);
    /// <summary>Branch coverage in percent, rounded down to one decimal.</summary>
    public double? BranchPercent => Percent(BranchRatio);

    /// <summary>Sum of two counts.</summary>
    public static CoverageCount operator +(CoverageCount a, CoverageCount b) =>
        new(a.CoveredLines + b.CoveredLines, a.CoverableLines + b.CoverableLines, a.CoveredBranches + b.CoveredBranches, a.Branches + b.Branches);

    /// <summary>Counts the given lines.</summary>
    public static CoverageCount Of(IEnumerable<LineHit> lines)
    {
        int covered = 0, coverable = 0, coveredBranches = 0, branches = 0;
        foreach (var hit in lines)
        {
            coverable++;
            if (hit.Hits > 0) covered++;
            coveredBranches += hit.CoveredBranches;
            branches += hit.Branches;
        }
        return new(covered, coverable, coveredBranches, branches);
    }

    /// <summary>Floor to one decimal.</summary>
    public static double? Percent(double? ratio) => ratio is { } value ? Math.Floor(value * 1000 + 1e-9) / 10 : null;
}

/// <summary>A line with branches of which some never ran.</summary>
/// <param name="Line">1-based line.</param>
/// <param name="Covered">Branch outcomes that ran.</param>
/// <param name="Total">Branch outcomes on the line.</param>
/// <param name="Code">The condition's source, when known.</param>
public sealed record BranchGap(int Line, int Covered, int Total, string? Code);

/// <summary>Coverage of one member.</summary>
/// <param name="Member">The member.</param>
/// <param name="Reported">False when the coverage data had no line of this member.</param>
/// <param name="Count">Covered and coverable lines and branches.</param>
/// <param name="UncoveredLines">Coverable lines that never ran.</param>
/// <param name="BranchGaps">Lines whose branches did not all run.</param>
public sealed record MemberCoverage(SourceMember Member, bool Reported, CoverageCount Count, IReadOnlyList<int> UncoveredLines, IReadOnlyList<BranchGap> BranchGaps)
{
    /// <summary>CRAP = CC² × (1 − c)³ + CC, with c the line coverage ratio (1 when nothing is coverable).</summary>
    public double Crap => Math.Round(Member.Complexity * (double)Member.Complexity * Math.Pow(1 - (Count.LineRatio ?? 1), 3) + Member.Complexity, 1);
}

/// <summary>Coverage of one type declaration (its own members and initializers, nested types excluded).</summary>
public sealed record TypeCoverage(SourceType Type, CoverageCount Count);

/// <summary>Coverage of one file.</summary>
/// <param name="File">The source file.</param>
/// <param name="Reported">True when the coverage data contained this file.</param>
/// <param name="Count">Counts over <paramref name="Lines"/>.</param>
/// <param name="Lines">Coverable lines with their hits, as judged (unreported code under 'zero' included as uncovered).</param>
public sealed record FileCoverage(SourceFile File, bool Reported, CoverageCount Count, IReadOnlyDictionary<int, LineHit> Lines);

/// <summary>Something that is not counted: an exclusion attribute, an unreported member under 'report'/'ignore'.</summary>
public sealed record SkippedCode(string Kind, string File, int Line, string? Type, string? Member, string Reason);

/// <summary>How code without coverage data is judged.</summary>
public static class UnreportedMode
{
    /// <summary>Counted as 0 % and listed.</summary>
    public const string Zero = "zero";
    /// <summary>Listed, not counted; the run is partial.</summary>
    public const string Report = "report";
    /// <summary>Left out and only counted (analyze only).</summary>
    public const string Ignore = "ignore";
    /// <summary>All values.</summary>
    public static readonly IReadOnlyList<string> All = [Zero, Report, Ignore];
}

/// <summary>Coverage data laid over the source model: what is coverable comes from the source, what ran from the data.</summary>
public sealed class CoverageModel
{
    CoverageModel(IReadOnlyList<FileCoverage> files, IReadOnlyList<MemberCoverage> members, IReadOnlyList<TypeCoverage> types,
        IReadOnlyList<SkippedCode> excluded, IReadOnlyList<SkippedCode> unreported, int unmapped)
    {
        Files = files;
        Members = members;
        Types = types;
        Excluded = excluded;
        Unreported = unreported;
        Unmapped = unmapped;
        Total = files.Aggregate(CoverageCount.Zero, (sum, file) => sum + file.Count);
        byPath = files.ToDictionary(f => f.File.Path, StringComparer.Ordinal);
    }

    readonly Dictionary<string, FileCoverage> byPath;

    /// <summary>Measured files.</summary>
    public IReadOnlyList<FileCoverage> Files { get; }
    /// <summary>Members that count (excluded and ignored members left out).</summary>
    public IReadOnlyList<MemberCoverage> Members { get; }
    /// <summary>Type declarations.</summary>
    public IReadOnlyList<TypeCoverage> Types { get; }
    /// <summary>Code removed by exclusion attributes.</summary>
    public IReadOnlyList<SkippedCode> Excluded { get; }
    /// <summary>Code without coverage data (counted as 0 % under 'zero').</summary>
    public IReadOnlyList<SkippedCode> Unreported { get; }
    /// <summary>Local functions the coverage engine did not instrument; neither counted nor 0 %.</summary>
    public IReadOnlyList<SkippedCode> Uninstrumented { get; private init; } = [];
    /// <summary>Report lines outside any member or type of the source, a sign that sources and report differ.</summary>
    public int Unmapped { get; }
    /// <summary>Counts over every measured file.</summary>
    public CoverageCount Total { get; }

    /// <summary>The file, or null when it is not measured.</summary>
    public FileCoverage? File(string path) => byPath.GetValueOrDefault(path);

    /// <summary>Lays <paramref name="data"/> (root-relative paths) over <paramref name="sources"/>.</summary>
    public static CoverageModel Build(IReadOnlyList<SourceFile> sources, CoverageData data, string unreported)
    {
        if (!UnreportedMode.All.Contains(unreported)) throw new ArgumentException($"Unknown unreported mode '{unreported}'");
        var layers = sources.Select(source => new FileLayer(source, data.Lines(source.Path), unreported)).ToArray();
        return new CoverageModel(layers.Select(l => l.Coverage).ToArray(), layers.SelectMany(l => l.Members).ToArray(),
            layers.SelectMany(l => l.Types).ToArray(), layers.SelectMany(l => l.Excluded).ToArray(), layers.SelectMany(l => l.Unreported).ToArray(),
            layers.Sum(l => l.Unmapped))
        { Uninstrumented = layers.SelectMany(l => l.Uninstrumented).ToArray() };
    }

    /// <summary>Only the types for which <paramref name="typeMatches"/> holds, their members and the files declaring them.</summary>
    public CoverageModel Restrict(Func<string, bool> typeMatches)
    {
        var types = Types.Where(t => typeMatches(t.Type.FullName)).ToArray();
        var paths = types.Select(t => t.Type.File).ToHashSet(StringComparer.Ordinal);
        var files = Files.Where(f => paths.Contains(f.File.Path)).ToArray();
        // File totals of a restricted run are the selected types' own lines.
        var restrictedFiles = files.Select(f =>
        {
            var lines = f.Lines.Where(pair => f.File.TypeAt(pair.Key) is { } type && typeMatches(type.FullName)).ToDictionary(pair => pair.Key, pair => pair.Value);
            return f with { Lines = lines, Count = CoverageCount.Of(lines.Values) };
        }).ToArray();
        return new CoverageModel(restrictedFiles, Members.Where(m => typeMatches(m.Member.Type)).ToArray(), types,
            Excluded.Where(e => e.Type is not null && typeMatches(e.Type)).ToArray(),
            Unreported.Where(e => e.Type is not null && typeMatches(e.Type)).ToArray(), Unmapped)
        { Uninstrumented = Uninstrumented.Where(e => e.Type is not null && typeMatches(e.Type)).ToArray() };
    }

    /// <summary>Counts over the given changed lines of each file (only lines that are coverable count).</summary>
    public CoverageCount CountLines(IReadOnlyDictionary<string, IReadOnlySet<int>> changed, out List<(string File, int Line, SourceMember? Member)> uncovered)
    {
        uncovered = [];
        var count = CoverageCount.Zero;
        foreach (var (path, lines) in changed)
        {
            if (File(path) is not { } file) continue;
            var hits = lines.Where(file.Lines.ContainsKey).Order().Select(line => (Line: line, Hit: file.Lines[line])).ToArray();
            count += CoverageCount.Of(hits.Select(item => item.Hit));
            uncovered.AddRange(hits.Where(item => item.Hit.Hits == 0).Select(item => (path, item.Line, file.File.MemberAt(item.Line))));
        }
        return count;
    }
}
