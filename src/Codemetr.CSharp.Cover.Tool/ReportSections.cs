using Codemetr.CSharp.Cover;

namespace Codemetr.CSharp.Cover.Tool;

/// <summary>The optional parts of a report: diff gaps, exclusions, worklist, per-type detail, hotspots.</summary>
internal static class ReportSections
{
    public static DiffSection? Diff(RunState state, int top) => state.Input.Diff is { } diff
        ? new DiffSection(
            diff.Uncovered.GroupBy(u => (u.File, u.Member?.Type, u.Member?.Name))
                .Select(g => new DiffGap(g.Key.File, g.Key.Type, g.Key.Name, Finding.Ranges(g.Select(u => u.Line)))).Take(top).ToArray(),
            state.Input.UnmeasuredChangedFiles,
            state.Result.RemovedMembers.Select(m => new RemovedMember(m.Type, m.Member, m.File)).ToArray())
        : null;

    public static IReadOnlyList<ExclusionItem> Exclusions(RunState state)
    {
        var baselined = state.Input.Baseline?.Exclusions.Select(e => e.Key).ToHashSet(StringComparer.Ordinal);
        return state.Input.Exclusions.Select(e =>
        {
            var site = e.Kind == ExclusionEntry.Attribute ? state.Model.Excluded.FirstOrDefault(x => x.Type == e.Type && x.Member == e.Member) : null;
            return new ExclusionItem(e.Kind, e.Type, e.Member, site?.File, site?.Line, e.Pattern, baselined?.Contains(e.Key));
        }).ToArray();
    }

    // The riskiest under-tested members: highest CRAP first.
    public static IReadOnlyList<string> Hotspots(CoverageModel model, int top) => model.Members
        .Where(m => m.Count.CoveredLines < m.Count.CoverableLines || m.Count.CoveredBranches < m.Count.Branches)
        .OrderByDescending(m => m.Crap).ThenBy(m => m.Count.LineRatio ?? 1).ThenBy(m => m.Member.File, StringComparer.Ordinal).ThenBy(m => m.Member.Line)
        .Take(Math.Max(0, top))
        .Select(m => $"{m.Member.File}:{m.Member.Line} {m.Member.Type}.{m.Member.Name}: CRAP {Finding.Number(m.Crap)}, line {ReportBuilder.Pct(m.Count.LinePercent)}, "
            + $"branch {ReportBuilder.Pct(m.Count.BranchPercent)}, complexity {m.Member.Complexity}{(m.Reported ? "" : ", no coverage data")}")
        .ToArray();
}
