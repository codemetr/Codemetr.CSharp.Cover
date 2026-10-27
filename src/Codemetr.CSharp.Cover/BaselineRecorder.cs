namespace Codemetr.CSharp.Cover;

/// <summary>Records a first baseline and tightens an existing one.</summary>
internal static class BaselineRecorder
{
    const double Epsilon = GuardText.Epsilon;

    public static CoverBaseline Initial(GuardInput input, GuardResult result, string? head) => new(CoverBaseline.CurrentSchema,
        new BaselineOrigin(head, input.Config.Effective.ByRule()),
        input.Model.Members.Select(Floor).DistinctBy(f => f.Key).ToArray(),
        result.Findings.Where(f => CoverRuleIds.Baselinable.Contains(f.Rule)).Select(Debt).ToArray(),
        input.Tests.Select(t => new SkipEntry(t.Project, t.Counts.Skipped)).ToArray(),
        input.Exclusions.ToArray());

    /// <summary>
    /// Applies only the safe changes: floors rise and are added for new code, fixed or improved debt shrinks, removed
    /// members and exclusions leave, skip counts fall. Lower floors, new or worse debt, new exclusions and more skips are
    /// refused; their count is returned.
    /// </summary>
    public static (CoverBaseline Baseline, int Refused) Tighten(CoverBaseline baseline, GuardInput input, GuardResult result, string? head)
    {
        // Findings that a baseline could record; run-level, diff and test findings are never recorded anyway.
        var refused = result.Findings.Count(f => f.Fails && f.Rule is not (CoverRuleIds.DiffLineCoverage or CoverRuleIds.DiffBranchCoverage
            or CoverRuleIds.LineCoverage or CoverRuleIds.BranchCoverage or CoverRuleIds.TestsFailed or CoverRuleIds.UnmeasuredChange));
        var inEffect = input.Exclusions.Select(e => e.Key).ToHashSet(StringComparer.Ordinal);
        return (baseline with
        {
            RecordedAt = new BaselineOrigin(head ?? baseline.RecordedAt.Head, StricterThresholds(baseline, input)),
            Members = RaisedFloors(baseline, input, result),
            Debt = ShrunkDebt(baseline, result),
            Tests = LowerSkips(baseline, input),
            Exclusions = input.Complete ? baseline.Exclusions.Where(e => inEffect.Contains(e.Key)).ToArray() : baseline.Exclusions,
        }, refused);
    }

    static MemberFloor[] RaisedFloors(CoverBaseline baseline, GuardInput input, GuardResult result)
    {
        var current = input.Model.Members.GroupBy(m => m.Member.Key, StringComparer.Ordinal).ToDictionary(g => g.Key, g => Floor(g.First()), StringComparer.Ordinal);
        var removed = result.RemovedMembers.Select(m => m.Key).ToHashSet(StringComparer.Ordinal);
        var members = new Dictionary<string, MemberFloor>(StringComparer.Ordinal);
        foreach (var floor in baseline.Members.Where(f => !removed.Contains(f.Key)))
            members[floor.Key] = current.TryGetValue(floor.Key, out var now)
                ? floor with { File = now.File, Line = Math.Max(floor.Line, now.Line), Branch = Max(floor.Branch, now.Branch) }
                : floor;
        foreach (var (key, floor) in current) members.TryAdd(key, floor);
        return members.Values.ToArray();
    }

    static DebtEntry[] ShrunkDebt(CoverBaseline baseline, GuardResult result)
    {
        var debt = baseline.Debt.ToList();
        foreach (var entry in result.Stale)
        {
            var index = debt.FindIndex(d => d.Rule == entry.Rule && d.Type == entry.Type && d.Member == entry.Member && Math.Abs(d.Value - entry.BaselineValue) < Epsilon);
            if (index < 0) continue;
            if (entry.CurrentValue is null) debt.RemoveAt(index);
            else debt[index] = debt[index] with { Value = entry.CurrentValue.Value };
        }
        return debt.ToArray();
    }

    static SkipEntry[] LowerSkips(CoverBaseline baseline, GuardInput input)
    {
        var tests = baseline.Tests.ToDictionary(t => t.Project, StringComparer.Ordinal);
        foreach (var project in input.Tests)
            tests[project.Project] = tests.TryGetValue(project.Project, out var old)
                ? old with { Skipped = Math.Min(old.Skipped, project.Counts.Skipped) }
                : new SkipEntry(project.Project, project.Counts.Skipped);
        return tests.Values.ToArray();
    }

    static Dictionary<string, double> StricterThresholds(CoverBaseline baseline, GuardInput input)
    {
        var thresholds = new Dictionary<string, double>(baseline.RecordedAt.Thresholds, StringComparer.Ordinal);
        foreach (var (rule, value) in input.Config.Effective.ByRule())
            thresholds[rule] = !thresholds.TryGetValue(rule, out var old) ? value
                : CoverRuleIds.HigherIsWorse(rule) ? Math.Min(old, value) : Math.Max(old, value);
        return thresholds;
    }

    static MemberFloor Floor(MemberCoverage m) => new(m.Member.Type, m.Member.Name, m.Member.File, m.Count.LinePercent ?? 100, m.Count.BranchPercent);
    static DebtEntry Debt(Finding f) => new(f.Rule, f.Type!, f.Member, f.File!, f.Value!.Value);
    static double? Max(double? a, double? b) => a is null ? b : b is null ? a : Math.Max(a.Value, b.Value);
}
