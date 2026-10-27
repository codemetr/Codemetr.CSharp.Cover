namespace Codemetr.CSharp.Cover;

/// <summary>Coverage of one source file: report lines attributed to members, plus unreported code as the mode says.</summary>
internal sealed class FileLayer
{
    readonly SourceFile source;
    readonly IReadOnlyDictionary<int, LineHit>? report;
    readonly string unreported;
    readonly SortedDictionary<int, LineHit> lines = new();
    readonly HashSet<int> reportedMembers = [];
    readonly HashSet<int> uninstrumentedMembers = [];

    public FileLayer(SourceFile source, IReadOnlyDictionary<int, LineHit>? report, string unreported)
    {
        this.source = source;
        this.report = report;
        this.unreported = unreported;
        TakeReportedLines();
        for (var index = 0; index < source.Members.Count; index++) JudgeMember(index);
        if (report is null && unreported == UnreportedMode.Zero) AddUnreportedInitializers();
        Excluded.AddRange(source.Types.Where(t => t.ExcludedBy is not null)
            .Select(t => new SkippedCode("attribute", source.Path, t.Line, t.FullName, null, t.ExcludedBy!)));
        Coverage = new FileCoverage(source, report is not null, CoverageCount.Of(lines.Values), lines);
        for (var index = 0; index < source.Members.Count; index++)
            if (Measure(index) is { } member) Members.Add(member);
        Types = source.Types.Where(t => t.ExcludedBy is null)
            .Select(t => new TypeCoverage(t, CoverageCount.Of(lines.Where(pair => source.TypeAt(pair.Key) == t).Select(pair => pair.Value)))).ToArray();
    }

    public FileCoverage Coverage { get; }
    public List<MemberCoverage> Members { get; } = [];
    public IReadOnlyList<TypeCoverage> Types { get; }
    public List<SkippedCode> Excluded { get; } = [];
    public List<SkippedCode> Unreported { get; } = [];
    public List<SkippedCode> Uninstrumented { get; } = [];
    public int Unmapped { get; private set; }

    void TakeReportedLines()
    {
        foreach (var (line, hit) in report ?? new Dictionary<int, LineHit>())
        {
            var index = source.MemberIndexAt(line);
            var type = source.TypeAt(line);
            if (index < 0 && type is null) { Unmapped++; continue; }
            if ((index >= 0 ? source.Members[index].ExcludedBy : type!.ExcludedBy) is not null) continue;
            if (index >= 0) reportedMembers.Add(index);
            lines[line] = hit;
        }
    }

    void JudgeMember(int index)
    {
        var member = source.Members[index];
        if (member.ExcludedBy is not null)
        {
            Excluded.Add(new SkippedCode("attribute", source.Path, member.Line, member.Type, member.Name, member.ExcludedBy));
            return;
        }
        if (reportedMembers.Contains(index) || member.CodeLines.Count == 0) return;
        if (report is not null && IsUninstrumented(index))
        {
            uninstrumentedMembers.Add(index);
            Uninstrumented.Add(new SkippedCode("uninstrumented", source.Path, member.Line, member.Type, member.Name,
                "local function without coverage data inside a measured member; the coverage engine skipped compiler-generated code"));
            return;
        }
        Unreported.Add(new SkippedCode("member", source.Path, member.Line, member.Type, member.Name,
            report is null ? "file has no coverage data" : "member has no coverage data"));
        if (unreported != UnreportedMode.Zero) return;
        // Unreported code: every estimated line uncovered, its decision points as uncovered branches.
        foreach (var line in member.CodeLines) lines.TryAdd(line, new LineHit(0, 0, 0));
        var first = member.CodeLines[0];
        lines[first] = lines[first] with { Branches = lines[first].Branches + Math.Max(0, member.Complexity - 1) };
    }

    void AddUnreportedInitializers()
    {
        foreach (var line in source.CodeLines.Where(l => source.MemberIndexAt(l) < 0 && source.TypeAt(l) is { ExcludedBy: null }))
            lines.TryAdd(line, new LineHit(0, 0, 0));
    }

    MemberCoverage? Measure(int index)
    {
        var member = source.Members[index];
        if (member.ExcludedBy is not null || uninstrumentedMembers.Contains(index)) return null;
        var isReported = reportedMembers.Contains(index);
        if (!isReported && (unreported != UnreportedMode.Zero || member.CodeLines.Count == 0)) return null;
        var own = lines.Where(pair => source.MemberIndexAt(pair.Key) == index).ToArray();
        var gaps = isReported
            ? own.Where(pair => pair.Value.CoveredBranches < pair.Value.Branches)
                .Select(pair => new BranchGap(pair.Key, pair.Value.CoveredBranches, pair.Value.Branches, source.Conditions.GetValueOrDefault(pair.Key))).ToArray()
            : [];
        return new MemberCoverage(member, isReported, CoverageCount.Of(own.Select(pair => pair.Value)),
            own.Where(pair => pair.Value.Hits == 0).Select(pair => pair.Key).ToArray(), gaps);
    }

    // A local function without data whose enclosing member has data was not instrumented, not left untested.
    bool IsUninstrumented(int index)
    {
        var member = source.Members[index];
        if (member.Kind != "localFunction") return false;
        var outer = source.Members.Select((m, i) => (Member: m, Index: i))
            .Where(c => c.Index != index && c.Member.Line <= member.Line && c.Member.EndLine >= member.EndLine)
            .OrderBy(c => c.Member.EndLine - c.Member.Line).FirstOrDefault();
        return outer.Member is not null && reportedMembers.Contains(outer.Index);
    }
}
