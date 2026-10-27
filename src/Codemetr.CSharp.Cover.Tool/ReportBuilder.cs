using System.Globalization;
using Codemetr.CSharp.Cover;

namespace Codemetr.CSharp.Cover.Tool;

/// <summary>Builds the report from a run: complete (for --output) and limited to --top (for stdout).</summary>
internal static class ReportBuilder
{
    /// <summary>Report format version: within one schema fields are only added and rule ids never renamed.</summary>
    public const int ReportSchema = 1;

    public static CoverReport Build(RunState state, bool complete)
    {
        var top = complete ? int.MaxValue : state.Request.Top;
        var locator = new TestLocator(state.Context.Root, state.TestProjects.Select(p => p.Directory.Length == 0 ? "." : p.Directory));
        var findings = Order(state.Result.Findings)
            .Select(f => f.Type is not null && f.Member is not null ? f with { TestIn = locator.For(f.Type) } : f).ToArray();
        var worklist = WorklistSectionBuilder.Worklist(state, locator, top);
        var model = state.Model;
        return new CoverReport
        {
            Version = CoverService.Version, Command = state.Command, Complete = state.Partial.Count == 0,
            Passed = state.ParseErrors.Count == 0 && (state.Command == CliOptions.Analyze || state.Result.Passed),
            Provenance = new Provenance(state.Evidence.Source, state.Git.Head, state.Git.Dirty, state.Context.ConfigFile, state.Context.Unreported,
                state.Evidence.Projects, state.Evidence.Reports.Select(r => r.Report).ToArray(), state.DiffInfo, state.Partial),
            Summary = Summary(state, findings, top, worklist),
            Violations = findings.Take(top).ToArray(),
            Diff = ReportSections.Diff(state, top),
            Unreported = model.Unreported.Take(top).ToArray(),
            Exclusions = ReportSections.Exclusions(state),
            Stale = state.Result.Stale,
            ParseErrors = state.ParseErrors,
            Warnings = state.Warnings,
            Hotspots = ReportSections.Hotspots(model, top),
            Worklist = worklist,
            Types = complete || state.Request.Member is not null ? TypeSection.Types(model, state.Request.Member) : null,
            BaselineWritten = state.BaselineWritten,
        };
    }

    static ReportSummary Summary(RunState state, Finding[] findings, int top, WorklistSection? worklist)
    {
        var model = state.Model;
        var diff = state.Input.Diff?.Count;
        return new ReportSummary
        {
            Files = model.Files.Count, Types = model.Types.Count, Members = model.Members.Count,
            Line = model.Total.LinePercent, Branch = model.Total.BranchPercent,
            CoveredLines = model.Total.CoveredLines, CoverableLines = model.Total.CoverableLines,
            CoveredBranches = model.Total.CoveredBranches, Branches = model.Total.Branches,
            DiffLine = diff?.LinePercent, DiffBranch = diff?.BranchPercent,
            Violations = findings.Length, New = findings.Count(f => f.Status == Finding.New), Worsened = findings.Count(f => f.Status == Finding.Worsened),
            Baselined = findings.Count(f => f.Status == Finding.Baselined), Stale = state.Result.Stale.Count, Tightenable = state.Result.Tightenable,
            Unreported = model.Unreported.Count, Excluded = model.Excluded.Count, Skipped = state.Skipped, Unmapped = model.Unmapped,
            Listed = Math.Min(top, findings.Length), Omitted = Math.Max(0, findings.Length - top),
            Below = worklist is null ? null : WorklistSectionBuilder.BelowCounts(state),
        };
    }

    // Failing findings first, then run-level ones, then by file and line.
    static IEnumerable<Finding> Order(IEnumerable<Finding> findings) => findings
        .OrderBy(f => f.Fails ? 0 : 1).ThenBy(f => f.File is null ? 0 : 1).ThenBy(f => f.File, StringComparer.Ordinal).ThenBy(f => f.Line ?? 0)
        .ThenBy(f => f.Rule, StringComparer.Ordinal).ThenBy(f => f.Member, StringComparer.Ordinal);

    internal static string Pct(double? value) => value is null ? "n/a" : value.Value.ToString("0.0", CultureInfo.InvariantCulture) + "%";
}
