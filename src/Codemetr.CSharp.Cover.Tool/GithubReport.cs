using System.Globalization;

namespace Codemetr.CSharp.Cover.Tool;

/// <summary>GitHub Actions workflow commands plus a job summary.</summary>
internal static class GithubReport
{
    public static void Write(CoverReport report, TextWriter output)
    {
        foreach (var f in report.Violations)
        {
            var location = f.File is null ? "" : $" file={Escape(f.File, true)}{(f.Line is { } line ? $",line={line}" : "")},";
            output.WriteLine($"::{(f.Fails ? "error" : "notice")}{(location.Length == 0 ? " " : location)}title={Escape("codemetr-csharp-cover " + f.Rule, true)}::{Escape(f.Message + " " + f.Hint, false)}");
        }
        foreach (var gap in report.Diff?.Uncovered ?? [])
            foreach (var range in gap.Lines)
                output.WriteLine($"::warning file={Escape(gap.File, true)},line={range[0]},endLine={range[1]},title=uncovered change::Changed lines are not covered by tests.");
        foreach (var item in report.Worklist?.Items ?? [])
            output.WriteLine($"::warning file={Escape(item.File, true)}{(item.Line is { } l ? $",line={l}" : "")},title=below coverage threshold::"
                + Escape($"{item.Type}{(item.Member is null ? "" : "." + item.Member)} line {ReportBuilder.Pct(item.LineCoverage)}, branch {ReportBuilder.Pct(item.BranchCoverage)}", false));
        foreach (var warning in report.Warnings) output.WriteLine($"::warning title=codemetr-csharp-cover::{Escape(warning, false)}");
        foreach (var error in report.InputErrors) output.WriteLine($"::error title=codemetr-csharp-cover {error.Code}::{Escape(error.Message, false)}");
        if (report.Summary is not { } s) return;
        output.WriteLine($"codemetr-csharp-cover {report.Command}: {(report.Passed ? "PASSED" : "FAILED")}; line {ReportBuilder.Pct(s.Line)}, branch {ReportBuilder.Pct(s.Branch)}, "
            + $"diff line {ReportBuilder.Pct(s.DiffLine)}, diff branch {ReportBuilder.Pct(s.DiffBranch)}");
        if (Environment.GetEnvironmentVariable("GITHUB_STEP_SUMMARY") is { Length: > 0 } summary) Summary(report, s, summary);
    }

    static void Summary(CoverReport report, ReportSummary s, string path)
    {
        using var writer = File.AppendText(path);
        writer.WriteLine($"### codemetr-csharp-cover {report.Command}: {(report.Passed ? "passed" : "failed")}{(report.Complete ? "" : " (partial)")}");
        writer.WriteLine();
        writer.WriteLine("| Line | Branch | Diff line | Diff branch | New | Worsened | Baselined | Stale |");
        writer.WriteLine("| --- | --- | --- | --- | --- | --- | --- | --- |");
        writer.WriteLine($"| {ReportBuilder.Pct(s.Line)} | {ReportBuilder.Pct(s.Branch)} | {ReportBuilder.Pct(s.DiffLine)} | {ReportBuilder.Pct(s.DiffBranch)} | {s.New} | {s.Worsened} | {s.Baselined} | {s.Stale} |");
        writer.WriteLine();
        foreach (var f in report.Violations.Where(v => v.Fails))
            writer.WriteLine($"- `{f.Rule}` {(f.File is null ? "" : $"`{f.File}{(f.Line is { } l ? ":" + l.ToString(CultureInfo.InvariantCulture) : "")}` ")}{f.Message}");
    }

    static string Escape(string value, bool property)
    {
        var escaped = value.Replace("%", "%25").Replace("\r", "%0D").Replace("\n", "%0A");
        return property ? escaped.Replace(":", "%3A").Replace(",", "%2C") : escaped;
    }
}
