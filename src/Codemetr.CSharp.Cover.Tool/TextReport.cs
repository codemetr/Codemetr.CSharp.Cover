using Codemetr.CSharp.Cover;

namespace Codemetr.CSharp.Cover.Tool;

/// <summary>Human- and agent-readable rendering of a report, grep-able as file:line: [status] rule.</summary>
internal static class TextReport
{
    public static void Write(CoverReport report, TextWriter output)
    {
        Header(report, output);
        foreach (var warning in report.Warnings) output.WriteLine("warning: " + warning);
        foreach (var error in report.ParseErrors) output.WriteLine($"{error.File}:{error.Line}: parse error {error.Id}: {error.Message}");
        foreach (var finding in report.Violations) Finding(finding, output);
        if (report.Summary!.Omitted > 0) output.WriteLine($"... {report.Summary.Omitted} more finding(s) (see --output FILE or raise --top)");
        DiffGaps(report.Diff, output);
        foreach (var entry in report.Stale)
            output.WriteLine($"{entry.File}: [stale] {entry.Rule} {entry.Type}{Dotted(entry.Member)}: {entry.Message}");
        if (report.Worklist is { } worklist) Worklist(worklist, output);
        foreach (var type in report.Types ?? []) Type(type, output);
        if (report.Hotspots.Count > 0 && report.Worklist is null && report.Types is null) Hotspots(report.Hotspots, output);
        if (report.BaselineWritten is not null) output.WriteLine($"Baseline written: {report.BaselineWritten}");
    }

    static void Header(CoverReport report, TextWriter output)
    {
        var s = report.Summary!;
        var p = report.Provenance!;
        var verdict = report.Command == CliOptions.Analyze ? $"{s.Violations} finding(s)" : report.Passed ? "PASSED" : "FAILED";
        var scope = report.Complete ? "complete run" : "PARTIAL: " + string.Join("; ", p.Partial);
        output.WriteLine($"codemetr-csharp-cover {report.Command}: {verdict} - {s.New} new, {s.Worsened} worsened, {s.Baselined} baselined, {s.Stale} stale - {scope}");
        var tests = p.TestProjects.Where(t => t.Tests is not null).Select(t => t.Tests!).Aggregate(TestCounts.None, (a, b) => a + b);
        var at = p.Head is null ? "" : " @ " + p.Head + (p.Dirty == true ? " (+working tree)" : "");
        output.WriteLine($"evidence: {p.Source}{at}; {p.TestProjects.Count} test project(s); {tests.Total} tests, {tests.Failed} failed, {tests.Skipped} skipped; unreported={p.Unreported}");
        var diff = p.Diff is null ? "no diff"
            : $"diff vs {p.Diff.Base}{(p.Diff.MergeBase is null ? "" : " (merge-base " + p.Diff.MergeBase + ")")}: line {Pct(s.DiffLine)}, branch {Pct(s.DiffBranch)}"
              + $" over {p.Diff.ChangedLines} changed line(s) in {p.Diff.Files} file(s)";
        output.WriteLine($"coverage: line {Pct(s.Line)} ({s.CoveredLines}/{s.CoverableLines}), branch {Pct(s.Branch)} ({s.CoveredBranches}/{s.Branches}); {diff}");
        output.WriteLine($"scope: {s.Files} files, {s.Types} types, {s.Members} members; exclusions {s.Excluded}; unreported {s.Unreported}; skipped {s.Skipped} generated/excluded files");
    }

    static void Finding(Finding f, TextWriter output)
    {
        var location = f.File is null ? f.Project ?? "" : f.Line is null ? f.File : $"{f.File}:{f.Line}";
        var status = f.Status == Codemetr.CSharp.Cover.Finding.Worsened ? $"worsened from {Codemetr.CSharp.Cover.Finding.Number(f.BaselineValue ?? 0)}" : f.Status;
        output.WriteLine($"{(location.Length == 0 ? "" : location + ": ")}[{status}] {f.Rule}: {f.Message}");
        if (f.Status == Codemetr.CSharp.Cover.Finding.Baselined) return;
        if (f.UncoveredLines is { Count: > 0 } lines)
            output.WriteLine($"    uncovered lines {Codemetr.CSharp.Cover.Finding.Format(lines)}{(f.MoreLines is { } more ? $" (+{more} more ranges)" : "")}");
        foreach (var branch in f.Branches ?? []) output.WriteLine($"    branch {Branch(branch)}");
        if (f.TestIn is not null) output.WriteLine($"    test in: {f.TestIn}");
        output.WriteLine($"    hint: {f.Hint}");
    }

    static void DiffGaps(DiffSection? diff, TextWriter output)
    {
        if (diff is not { Uncovered.Count: > 0 }) return;
        output.WriteLine("Uncovered changed lines:");
        foreach (var gap in diff.Uncovered)
            output.WriteLine($"  {gap.File}:{Codemetr.CSharp.Cover.Finding.Format(gap.Lines)}{(gap.Member is null ? "" : $" {gap.Type}.{gap.Member}")}");
    }

    static void Worklist(WorklistSection w, TextWriter output)
    {
        var branch = w.Thresholds.Branch is { } b ? $" or branch {Codemetr.CSharp.Cover.Finding.Number(b)}%" : "";
        output.WriteLine($"WORKLIST (not a gate result): {w.Total} {w.Level}(s) below line {Codemetr.CSharp.Cover.Finding.Number(w.Thresholds.Line)}%{branch}"
            + $" [{w.Thresholds.Source}], sorted by {w.Sort}{(w.Complete ? "" : ", partial run")}");
        var index = 0;
        foreach (var item in w.Items)
        {
            var name = item.Level == "file" ? item.File : $"{item.File}:{item.Line} {item.Type}{Dotted(item.Member)}";
            var crap = item.Crap is { } c ? $"  CRAP {Codemetr.CSharp.Cover.Finding.Number(c)}" : "";
            output.WriteLine($"  {++index}. {name}  line {Pct(item.LineCoverage)} branch {Pct(item.BranchCoverage)}  gap {item.Gap.Lines}L {item.Gap.Branches}B{crap}"
                + $"{(item.Reported ? "" : "  [no coverage data]")}{(item.Gate == "none" ? "" : $"  [{item.Gate}]")}");
            if (item.UncoveredLines is { Count: > 0 } lines) output.WriteLine($"     uncovered {Codemetr.CSharp.Cover.Finding.Format(lines)}");
            if (item.TestIn is not null) output.WriteLine($"     test in: {item.TestIn}");
        }
        if (w.Omitted > 0) output.WriteLine($"  ... {w.Omitted} more (see --output FILE or raise --top)");
    }

    static void Type(TypeReport type, TextWriter output)
    {
        output.WriteLine($"{type.File}:{type.Line} {type.Type}: line {Pct(type.Coverage.Line)}, branch {Pct(type.Coverage.Branch)}");
        foreach (var m in type.Members)
        {
            output.WriteLine($"  {m.Line}: {m.Member}: line {Pct(m.Coverage.Line)} ({m.CoveredLines}/{m.CoverableLines}), branch {Pct(m.Coverage.Branch)} "
                + $"({m.CoveredBranches}/{m.Branches}), complexity {m.Complexity}, CRAP {Codemetr.CSharp.Cover.Finding.Number(m.Crap)}{(m.Reported ? "" : ", no coverage data")}");
            if (m.UncoveredLines.Count > 0) output.WriteLine($"     uncovered {Codemetr.CSharp.Cover.Finding.Format(m.UncoveredLines)}");
            foreach (var b in m.BranchGaps) output.WriteLine($"     branch {Branch(b)}");
        }
    }

    static void Hotspots(IReadOnlyList<string> hotspots, TextWriter output)
    {
        output.WriteLine("Riskiest under-tested members:");
        for (var index = 0; index < hotspots.Count; index++) output.WriteLine($"  {index + 1}. {hotspots[index]}");
    }

    public static void WriteDoctor(DoctorReport report, TextWriter output)
    {
        output.WriteLine($"codemetr-csharp-cover doctor: {(report.Passed ? "ready" : "NOT READY")} - {report.TestProjects.Count} test project(s)");
        foreach (var project in report.TestProjects)
            output.WriteLine($"  {project.Project}: {project.Platform}, {string.Join("/", project.Frameworks.DefaultIfEmpty("unknown framework"))}, "
                + $"{project.Engine ?? "no coverage engine"}{(project.Fix is null ? "" : $"\n    fix: {project.Fix}")}");
        foreach (var error in report.InputErrors) output.WriteLine($"error [{error.Code}]: {error.Message}{(error.Hint is null ? "" : "\n    hint: " + error.Hint)}");
        foreach (var warning in report.Warnings) output.WriteLine("warning: " + warning);
    }

    static string Branch(BranchInfo branch) => $"{branch.Line}{(branch.Code is null ? "" : $" `{branch.Code}`")}: {branch.Covered} of {branch.Total} outcomes ran";
    static string Dotted(string? member) => member is null ? "" : "." + member;
    static string Pct(double? value) => ReportBuilder.Pct(value);
}
