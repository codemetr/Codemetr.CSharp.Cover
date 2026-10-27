using System.Text.Json;
using Codemetr.CSharp.Cover.Tool;

namespace Codemetr.CSharp.Cover.Tests;

public sealed class CliTests
{
    const string Source = """
        namespace Shop;

        public class Calc
        {
            public int Abs(int a)
            {
                if (a < 0)
                    return -a;
                return a;
            }

            public int Never() => 1;
        }
        """;

    static async Task<(int Exit, string Out, string Err)> Run(params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var exit = await Program.RunAsync(args, new StringReader(""), output, error);
        return (exit, output.ToString(), error.ToString());
    }

    static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement;

    // A repository with one source file and a coverage report plus test results "from a run" of it.
    static TempDirectory Repository(bool neverCovered = false, int failed = 0)
    {
        var dir = new TempDirectory();
        dir.Write("src/Calc.cs", Source);
        var root = dir.Path.Replace('\\', '/');
        var never = neverCovered ? 1 : 0;
        dir.Write("results/coverage.cobertura.xml", $"""
            <coverage><sources><source>{root}</source></sources><packages><package name="Lib"><classes>
              <class name="Shop.Calc" filename="src/Calc.cs"><lines>
                <line number="6" hits="1" /><line number="7" hits="1" branch="True" condition-coverage="50% (1/2)" />
                <line number="8" hits="0" /><line number="9" hits="1" /><line number="10" hits="1" /><line number="12" hits="{never}" />
              </lines></class>
            </classes></package></packages></coverage>
            """);
        dir.Write("results/run.trx", $"""
            <TestRun><ResultSummary><Counters total="{2 + failed}" passed="2" failed="{failed}" notExecuted="0" /></ResultSummary></TestRun>
            """);
        return dir;
    }

    static string[] Reports(TempDirectory dir, params string[] extra) =>
        ["--root", dir.Path, "--reports", "results/*.xml", "--test-results", "results/*.trx", "--diff-file", Path.Combine(dir.Path, "none.diff"), .. extra];

    [Fact]
    public async Task UsageErrorsExitWithTwo()
    {
        Assert.Equal(2, (await Run()).Exit);
        Assert.Equal(0, (await Run("--help")).Exit);
        Assert.Equal(2, (await Run("bogus")).Exit);
        Assert.Equal(2, (await Run("check", "--below", "80")).Exit);
        Assert.Equal(2, (await Run("check", "--unreported", "ignore")).Exit);
        Assert.Equal(2, (await Run("check", "--test-results", "x.trx")).Exit);
        Assert.Equal(2, (await Run("analyze", "--top", "-1")).Exit);
    }

    [Fact]
    public async Task CheckJudgesReportsWithTestResults()
    {
        using var dir = Repository();
        dir.Write("none.diff", "");
        var (exit, output, _) = await Run(["check", .. Reports(dir, "--format", "json")]);
        var report = Json(output);
        Assert.Equal(0, exit);
        Assert.True(report.GetProperty("passed").GetBoolean());
        Assert.Equal("reports", report.GetProperty("provenance").GetProperty("source").GetString());
        var summary = report.GetProperty("summary");
        Assert.Equal(4, summary.GetProperty("coveredLines").GetInt32());
        Assert.Equal(6, summary.GetProperty("coverableLines").GetInt32());
    }

    [Fact]
    public async Task DiffCoverageOfChangedLinesDecidesTheGate()
    {
        using var dir = Repository();
        dir.Write("none.diff", "--- a/src/Calc.cs\n+++ b/src/Calc.cs\n@@ -7,0 +8,1 @@\n+                return -a;\n");
        var (exit, output, _) = await Run(["check", .. Reports(dir, "--format", "json")]);
        Assert.Equal(1, exit);
        var report = Json(output);
        Assert.Contains(report.GetProperty("violations").EnumerateArray(), v => v.GetProperty("rule").GetString() == "diffLineCoverage");
        var gap = report.GetProperty("diff").GetProperty("uncovered")[0];
        Assert.Equal("Abs(int)", gap.GetProperty("member").GetString());

        var text = await Run(["check", .. Reports(dir, "--format", "text")]);
        Assert.Contains("[new] diffLineCoverage", text.Out);
        Assert.Contains("src/Calc.cs:8 Shop.Calc.Abs(int)", text.Out);
    }

    [Fact]
    public async Task ReportsWithoutTestResultsOrWithFailedTestsCannotPass()
    {
        using var dir = Repository(failed: 1);
        dir.Write("none.diff", "");
        var missing = await Run("check", "--root", dir.Path, "--reports", "results/*.xml");
        Assert.Equal(2, missing.Exit);
        Assert.Equal("missingTestResults", Json(missing.Out).GetProperty("inputErrors")[0].GetProperty("code").GetString());

        var failing = await Run(["check", .. Reports(dir)]);
        Assert.Equal(1, failing.Exit);
        Assert.Contains("testsFailed", failing.Out);

        // analyze reports, never fails on findings.
        Assert.Equal(0, (await Run(["analyze", .. Reports(dir)])).Exit);
    }

    [Fact]
    public async Task ACoverageReportOlderThanItsSourceIsStale()
    {
        using var dir = Repository();
        dir.Write("none.diff", "");
        File.SetLastWriteTimeUtc(Path.Combine(dir.Path, "results/coverage.cobertura.xml"), DateTime.UtcNow.AddHours(-1));
        var stale = await Run(["check", .. Reports(dir)]);
        Assert.Equal(2, stale.Exit);
        Assert.Contains("staleCoverage", stale.Out);
        var analyze = await Run(["analyze", .. Reports(dir)]);
        Assert.Equal(0, analyze.Exit);
        Assert.Contains("old code", analyze.Err);
    }

    [Fact]
    public async Task BaselineRatchetsMemberCoverage()
    {
        using var dir = Repository(neverCovered: true);
        dir.Write("none.diff", "");
        var recorded = await Run(["baseline", .. Reports(dir)]);
        Assert.Equal(0, recorded.Exit);
        Assert.True(File.Exists(Path.Combine(dir.Path, "codemetr-cover.baseline.json")));
        Assert.Equal(0, (await Run(["check", .. Reports(dir)])).Exit);

        // The test that covered Never() is gone: its floor fails, and baseline refuses to lower it.
        dir.Write("results/coverage.cobertura.xml", File.ReadAllText(Path.Combine(dir.Path, "results/coverage.cobertura.xml")).Replace("number=\"12\" hits=\"1\"", "number=\"12\" hits=\"0\""));
        var dropped = await Run(["check", .. Reports(dir)]);
        Assert.Equal(1, dropped.Exit);
        Assert.Contains("coverageDropped", dropped.Out);
        Assert.Equal(1, (await Run(["baseline", .. Reports(dir)])).Exit);
        Assert.Equal(1, (await Run(["check", .. Reports(dir)])).Exit);

        // A partial run never records a baseline.
        Assert.Equal(2, (await Run(["baseline", .. Reports(dir, "--type", "Calc")])).Exit);
    }

    [Fact]
    public async Task WorklistListsEveryMemberBelowTheThresholdAndNeverDecides()
    {
        using var dir = Repository();
        dir.Write("none.diff", "");
        var (exit, output, _) = await Run(["analyze", .. Reports(dir, "--below", "90,60", "--format", "json")]);
        Assert.Equal(0, exit);
        var worklist = Json(output).GetProperty("worklist");
        Assert.Equal(JsonValueKind.Null, worklist.GetProperty("verdict").ValueKind);
        Assert.Equal(2, worklist.GetProperty("total").GetInt32());
        // Largest gap first: Abs misses one line and one branch, Never one line.
        Assert.Equal("Abs(int)", worklist.GetProperty("items")[0].GetProperty("member").GetString());

        var top = Json((await Run(["analyze", .. Reports(dir, "--below", "90,60", "--top", "1")])).Out).GetProperty("worklist");
        Assert.Equal(1, top.GetProperty("listed").GetInt32());
        Assert.Equal(1, top.GetProperty("omitted").GetInt32());
    }

    [Fact]
    public async Task OutputFileHoldsTheCompleteReport()
    {
        using var dir = Repository();
        dir.Write("none.diff", "");
        var file = Path.Combine(dir.Path, "full.json");
        var (exit, _, _) = await Run(["analyze", .. Reports(dir, "--top", "0", "--output", file)]);
        Assert.Equal(0, exit);
        var types = Json(File.ReadAllText(file)).GetProperty("types");
        Assert.Equal(2, types[0].GetProperty("members").GetArrayLength());
    }
}
