using System.Globalization;

namespace Codemetr.CSharp.Cover;

/// <summary>Coverage of the changed lines.</summary>
/// <param name="Base">The base the diff was taken against, as given.</param>
/// <param name="Count">Counts over changed coverable lines.</param>
/// <param name="Uncovered">Changed coverable lines that never ran.</param>
public sealed record DiffCoverage(string Base, CoverageCount Count, IReadOnlyList<(string File, int Line, SourceMember? Member)> Uncovered);

/// <summary>Test outcome of one test project.</summary>
public sealed record ProjectTests(string Project, TestCounts Counts);

/// <summary>Everything the gate judges.</summary>
public sealed record GuardInput
{
    /// <summary>Coverage over the measured sources.</summary>
    public required CoverageModel Model { get; init; }
    /// <summary>Configuration.</summary>
    public required CoverConfig Config { get; init; }
    /// <summary>False for a narrowed run: totals and removals are not judged then.</summary>
    public required bool Complete { get; init; }
    /// <summary>Diff coverage, when a diff was taken.</summary>
    public DiffCoverage? Diff { get; init; }
    /// <summary>Test results per project; empty when unknown.</summary>
    public IReadOnlyList<ProjectTests> Tests { get; init; } = [];
    /// <summary>The baseline, when one exists.</summary>
    public CoverBaseline? Baseline { get; init; }
    /// <summary>Exclusions in effect (attributes on code and configuration globs).</summary>
    public IReadOnlyList<ExclusionEntry> Exclusions { get; init; } = [];
    /// <summary>Changed C# files that are never measured (generated or excluded).</summary>
    public IReadOnlyList<string> UnmeasuredChangedFiles { get; init; } = [];
    /// <summary>True when a type of this full name is declared in code that is never measured.</summary>
    public Func<string, bool> DeclaredInUnmeasuredCode { get; init; } = _ => false;
}

/// <summary>A recorded entry that the baseline should drop or tighten; stale debt fails the gate like in codemetr-csharp.</summary>
public sealed record StaleEntry(string Rule, string Type, string? Member, string File, double BaselineValue, double? CurrentValue, string Message);

/// <summary>Result of the gate.</summary>
/// <param name="Findings">Findings in a stable order.</param>
/// <param name="Stale">Recorded debt that improved or disappeared (fails until the baseline is tightened).</param>
/// <param name="Tightenable">Floors that could rise and recorded members that are gone; informational.</param>
/// <param name="RemovedMembers">Recorded members that no longer exist.</param>
public sealed record GuardResult(IReadOnlyList<Finding> Findings, IReadOnlyList<StaleEntry> Stale, int Tightenable, IReadOnlyList<MemberFloor> RemovedMembers)
{
    /// <summary>True when nothing fails.</summary>
    public bool Passed => Findings.All(f => !f.Fails) && Stale.Count == 0;
}

/// <summary>Evaluates thresholds, diff coverage, tests, exclusions and the ratchet.</summary>
public static class CoverGuard
{
    const double Epsilon = GuardText.Epsilon;

    /// <summary>Judges one run.</summary>
    public static GuardResult Evaluate(GuardInput input)
    {
        var findings = new List<Finding>();
        var model = input.Model;
        var rules = input.Config.Effective;
        var baseline = input.Baseline;

        if (input.Complete) RunThresholds(findings, model.Total, rules);
        if (input.Diff is { } diff) DiffThresholds(findings, diff, rules);

        // Per-entity rules, matched against recorded debt.
        var current = new List<Finding>();
        foreach (var member in model.Members)
        {
            var memberRules = input.Config.RulesFor(member.Member.File, member.Member.Type);
            var name = $"{member.Member.Type}.{member.Member.Name}";
            if (memberRules.MemberLineCoverage is > 0 and var minimum && member.Count.LineRatio is { } ratio && ratio * 100 < minimum - Epsilon)
                current.Add(Entity(CoverRuleIds.MemberLineCoverage, member, member.Count.LinePercent!.Value, minimum,
                    $"{name}: line coverage {Pct(member.Count.LinePercent)} below min {Finding.Number(minimum)}%."));
            if (memberRules.CrapScore is { } max && member.Crap > max + Epsilon)
                current.Add(Entity(CoverRuleIds.CrapScore, member, member.Crap, max,
                    $"{name}: CRAP {Finding.Number(member.Crap)} exceeds max {Finding.Number(max)} (complexity {member.Member.Complexity}, line coverage {Pct(member.Count.LinePercent)})."));
        }
        foreach (var type in model.Types)
        {
            var typeRules = input.Config.RulesFor(type.Type.File, type.Type.FullName);
            if (typeRules.TypeLineCoverage is > 0 and var minimum && type.Count.LineRatio is { } ratio && ratio * 100 < minimum - Epsilon)
                current.Add(new Finding
                {
                    Rule = CoverRuleIds.TypeLineCoverage, Status = Finding.New, Type = type.Type.FullName, File = type.Type.File, Line = type.Type.Line,
                    Value = type.Count.LinePercent, Limit = minimum, Coverage = CoveragePercent.Of(type.Count),
                    Message = $"{type.Type.FullName}: line coverage {Pct(type.Count.LinePercent)} below min {Finding.Number(minimum)}%.",
                    Hint = CoverRuleIds.Hint(CoverRuleIds.TypeLineCoverage),
                });
        }
        var measured = model.Files.Select(f => f.File.Path).ToHashSet(StringComparer.Ordinal);
        var stale = new List<StaleEntry>();
        findings.AddRange(DebtMatcher.Match(current, baseline, measured, input, model, stale));

        var removed = new List<MemberFloor>();
        var tightenable = 0;
        if (baseline is not null)
        {
            tightenable += FloorRatchet.Floors(findings, model, baseline, input, removed);
            tightenable += FloorRatchet.Inventory(findings, input, baseline);
            FloorRatchet.ThresholdsLowered(findings, rules, baseline);
        }
        Tests(findings, input.Tests, baseline);
        foreach (var file in input.UnmeasuredChangedFiles)
            findings.Add(new Finding
            {
                Rule = CoverRuleIds.UnmeasuredChange, Status = Finding.New, File = file,
                Message = $"{file}: changed, but generated or excluded code is never measured.", Hint = CoverRuleIds.Hint(CoverRuleIds.UnmeasuredChange),
            });
        return new GuardResult(findings, stale, tightenable, removed);
    }

    static void RunThresholds(List<Finding> findings, CoverageCount total, CoverRules rules)
    {
        if (rules.LineCoverage is > 0 and var line && total.LineRatio is { } lr && lr * 100 < line - Epsilon)
            findings.Add(Run(CoverRuleIds.LineCoverage, total.LinePercent!.Value, line, $"Line coverage {Pct(total.LinePercent)} ({total.CoveredLines} of {total.CoverableLines}) below min {Finding.Number(line)}%."));
        if (rules.BranchCoverage is > 0 and var branch && total.BranchRatio is { } br && br * 100 < branch - Epsilon)
            findings.Add(Run(CoverRuleIds.BranchCoverage, total.BranchPercent!.Value, branch, $"Branch coverage {Pct(total.BranchPercent)} ({total.CoveredBranches} of {total.Branches}) below min {Finding.Number(branch)}%."));
    }

    static void DiffThresholds(List<Finding> findings, DiffCoverage diff, CoverRules rules)
    {
        var count = diff.Count;
        if (rules.DiffLineCoverage is > 0 and var line && count.LineRatio is { } lr && lr * 100 < line - Epsilon)
            findings.Add(Run(CoverRuleIds.DiffLineCoverage, count.LinePercent!.Value, line,
                $"Changed lines since {diff.Base}: line coverage {Pct(count.LinePercent)} ({count.CoveredLines} of {count.CoverableLines}) below min {Finding.Number(line)}%."));
        if (rules.DiffBranchCoverage is > 0 and var branch && count.BranchRatio is { } br && br * 100 < branch - Epsilon)
            findings.Add(Run(CoverRuleIds.DiffBranchCoverage, count.BranchPercent!.Value, branch,
                $"Changed lines since {diff.Base}: branch coverage {Pct(count.BranchPercent)} ({count.CoveredBranches} of {count.Branches}) below min {Finding.Number(branch)}%."));
    }

    static Finding Run(string rule, double value, double limit, string message) =>
        new() { Rule = rule, Status = Finding.New, Value = value, Limit = limit, Message = message, Hint = CoverRuleIds.Hint(rule) };

    static Finding Entity(string rule, MemberCoverage member, double value, double limit, string message) => new Finding
    {
        Rule = rule, Status = Finding.New, Type = member.Member.Type, Member = member.Member.Name, File = member.Member.File,
        Line = member.Member.Line, Value = value, Limit = limit, Message = message, Hint = CoverRuleIds.Hint(rule),
    }.WithDetails(member);

    static void Tests(List<Finding> findings, IReadOnlyList<ProjectTests> tests, CoverBaseline? baseline)
    {
        var skipped = (baseline?.Tests ?? []).ToDictionary(t => t.Project, t => t.Skipped, StringComparer.Ordinal);
        foreach (var project in tests)
        {
            if (project.Counts.Failed > 0)
                findings.Add(new Finding
                {
                    Rule = CoverRuleIds.TestsFailed, Status = Finding.New, Project = project.Project, Value = project.Counts.Failed,
                    Message = $"{project.Project}: {project.Counts.Failed} of {project.Counts.Total} test(s) failed.", Hint = CoverRuleIds.Hint(CoverRuleIds.TestsFailed),
                });
            if (skipped.TryGetValue(project.Project, out var recorded) ? project.Counts.Skipped > recorded : baseline is not null && project.Counts.Skipped > 0)
                findings.Add(new Finding
                {
                    Rule = CoverRuleIds.SkippedTestsIncreased, Status = Finding.New, Project = project.Project, Value = project.Counts.Skipped,
                    Limit = skipped.GetValueOrDefault(project.Project),
                    Message = $"{project.Project}: {project.Counts.Skipped} skipped test(s), recorded {skipped.GetValueOrDefault(project.Project)}.",
                    Hint = CoverRuleIds.Hint(CoverRuleIds.SkippedTestsIncreased),
                });
        }
    }

    /// <summary>The first baseline of a code base: current floors, debt, skip counts and exclusions.</summary>
    public static CoverBaseline Initial(GuardInput input, GuardResult result, string? head) => BaselineRecorder.Initial(input, result, head);

    /// <summary>
    /// Applies only the safe changes: floors rise and are added for new code, fixed or improved debt shrinks, removed
    /// members and exclusions leave, skip counts fall. Lower floors, new or worse debt, new exclusions and more skips are
    /// refused; their count is returned.
    /// </summary>
    public static (CoverBaseline Baseline, int Refused) Tighten(CoverBaseline baseline, GuardInput input, GuardResult result, string? head) =>
        BaselineRecorder.Tighten(baseline, input, result, head);

    static string Pct(double? value) => GuardText.Pct(value);
}
