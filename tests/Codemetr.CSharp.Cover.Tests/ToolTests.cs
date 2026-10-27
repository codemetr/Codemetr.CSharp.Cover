using System.Diagnostics;
using System.Text.Json;
using Codemetr.CSharp.Cover.Tool;

namespace Codemetr.CSharp.Cover.Tests;

public sealed class ToolTests
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

    static TempDirectory Repository()
    {
        var dir = new TempDirectory();
        dir.Write("src/Calc.cs", Source);
        var root = dir.Path.Replace('\\', '/');
        dir.Write("results/coverage.cobertura.xml", $"""
            <coverage><sources><source>{root}</source></sources><packages><package name="Lib"><classes>
              <class name="Shop.Calc" filename="src/Calc.cs"><lines>
                <line number="6" hits="1" /><line number="7" hits="1" branch="True" condition-coverage="50% (1/2)" />
                <line number="8" hits="0" /><line number="9" hits="1" /><line number="10" hits="1" /><line number="12" hits="0" />
              </lines></class>
            </classes></package></packages></coverage>
            """);
        dir.Write("results/run.trx", """<TestRun><ResultSummary><Counters total="2" passed="2" failed="0" notExecuted="0" /></ResultSummary></TestRun>""");
        return dir;
    }

    static string[] Reports(TempDirectory dir, params string[] extra) =>
        ["--root", dir.Path, "--reports", "results/*.xml", "--test-results", "results/*.trx", .. extra];

    static void Git(string directory, params string[] args)
    {
        var start = new ProcessStartInfo("git") { WorkingDirectory = directory, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in new[] { "-c", "user.email=test@example.invalid", "-c", "user.name=test", "-c", "core.autocrlf=false" }.Concat(args)) start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, process.StandardError.ReadToEnd());
    }

    [Fact]
    public async Task DiffAgainstGitCountsUncommittedAndUntrackedChanges()
    {
        using var dir = Repository();
        Git(dir.Path, "init", "-q");
        Git(dir.Path, "add", "src");
        Git(dir.Path, "commit", "-q", "-m", "init");
        // An untracked file is wholly new code; its coverage data is missing, so it counts as 0 %.
        dir.Write("src/New.cs", "namespace Shop;\npublic class New\n{\n    public int M() => 2;\n}\n");
        File.SetLastWriteTimeUtc(Path.Combine(dir.Path, "results/coverage.cobertura.xml"), DateTime.UtcNow.AddMinutes(1));
        var (exit, output, error) = await Run(["check", .. Reports(dir, "--base", "HEAD", "--format", "json")]);
        Assert.True(exit == 1, error + output);
        var report = JsonDocument.Parse(output).RootElement;
        var diff = report.GetProperty("provenance").GetProperty("diff");
        Assert.Equal("HEAD", diff.GetProperty("base").GetString());
        Assert.Equal(1, diff.GetProperty("files").GetInt32());
        Assert.Contains(report.GetProperty("violations").EnumerateArray(), v => v.GetProperty("rule").GetString() == "diffLineCoverage");

        var missing = await Run(["check", .. Reports(dir, "--base", "no-such-branch")]);
        Assert.Equal(2, missing.Exit);
        Assert.Contains("unresolvedBase", missing.Out);
    }

    [Fact]
    public async Task GithubFormatWritesAnnotationsAndAJobSummary()
    {
        using var dir = Repository();
        dir.Write("change.diff", "--- a/src/Calc.cs\n+++ b/src/Calc.cs\n@@ -7,0 +8,1 @@\n+                return -a;\n");
        var summary = Path.Combine(dir.Path, "summary.md");
        Environment.SetEnvironmentVariable("GITHUB_STEP_SUMMARY", summary);
        try
        {
            var (exit, output, _) = await Run(["check", .. Reports(dir, "--diff-file", Path.Combine(dir.Path, "change.diff"), "--format", "github")]);
            Assert.Equal(1, exit);
            Assert.Contains("::error title=codemetr-csharp-cover diffLineCoverage::", output);
            Assert.Contains("::warning file=src/Calc.cs,line=8,endLine=8,title=uncovered change::", output);
            Assert.Contains("| Line | Branch |", File.ReadAllText(summary));
        }
        finally { Environment.SetEnvironmentVariable("GITHUB_STEP_SUMMARY", null); }
    }

    [Fact]
    public async Task TextFormatShowsWorklistAndMemberDetail()
    {
        using var dir = Repository();
        var worklist = await Run(["analyze", .. Reports(dir, "--below", "90,60", "--format", "text")]);
        Assert.Equal(0, worklist.Exit);
        Assert.Contains("WORKLIST (not a gate result): 2 member(s) below line 90% or branch 60% [cli], sorted by gap", worklist.Out);
        Assert.Contains("src/Calc.cs:12 Shop.Calc.Never()", worklist.Out);

        var member = await Run(["analyze", .. Reports(dir, "--member", "Abs", "--format", "text")]);
        Assert.Contains("Abs(int): line 80.0% (4/5), branch 50.0% (1/2)", member.Out);
        Assert.Contains("branch 7 `if (a < 0)`: 1 of 2 outcomes ran", member.Out);

        var hotspots = await Run(["analyze", .. Reports(dir, "--format", "text")]);
        Assert.Contains("Riskiest under-tested members:", hotspots.Out);

        dir.Write("codemetr-cover.json", """{ "worklist": { "line": 50, "level": "file" } }""");
        var configured = await Run(["analyze", .. Reports(dir, "--below", "--format", "json")]);
        var section = JsonDocument.Parse(configured.Out).RootElement.GetProperty("worklist");
        Assert.Equal("config", section.GetProperty("thresholds").GetProperty("source").GetString());
        Assert.Equal("file", section.GetProperty("level").GetString());
    }

    [Fact]
    public async Task DoctorReportsAMissingCoverageEngineWithItsFix()
    {
        using var dir = new TempDirectory();
        var empty = await Run("doctor", "--root", dir.Path);
        Assert.Equal(2, empty.Exit);
        Assert.Contains("noTestProjects", empty.Out);
        var text = await Run("doctor", "--root", dir.Path, "--format", "text");
        Assert.Contains("NOT READY", text.Out);
    }

    [Theory]
    [InlineData("vstest", new[] { "Microsoft.NET.Test.Sdk", "xunit" }, "coverletMissing", "dotnet add tests/A/A.csproj package coverlet.collector")]
    [InlineData("mtp", new[] { "xunit.v3.mtp-v2", "coverlet.collector" }, "wrongCoverageEngine", "package coverlet.MTP")]
    public void TestProjectProblemsNameTheExactFix(string platform, string[] packages, string code, string fix)
    {
        var project = new TestProject("tests/A/A.csproj", "tests/A", "A", "A.dll", platform, packages, []);
        var problem = Assert.Single(project.Problems());
        Assert.Equal(code, problem.Code);
        Assert.Contains(fix, problem.Fix);
        Assert.Null(project.Engine);
        Assert.NotEmpty(project.Frameworks);
        Assert.Empty((project with { Packages = [.. packages, project.RequiredEngine] }).Problems());
    }

    [Fact]
    public void FrameworksAreRecognisedByPackage()
    {
        TestProject With(params string[] packages) => new("p.csproj", "", "p", "p.dll", "vstest", packages, []);
        Assert.Equal(["xunit.v3"], With("xunit.v3.mtp-v2").Frameworks);
        Assert.Equal(["xunit"], With("xunit").Frameworks);
        Assert.Equal(["nunit"], With("NUnit").Frameworks);
        Assert.Equal(["mstest"], With("MSTest").Frameworks);
        Assert.Equal(["tunit"], With("TUnit").Frameworks);
    }

    [Fact]
    public async Task ATestTargetMustExistAndHaveAKnownKind()
    {
        using var dir = new TempDirectory();
        dir.Write("notes.txt", "");
        Assert.Contains("Test target not found", (await Run("doctor", "--root", dir.Path, "--test", "missing.slnx")).Err);
        Assert.Contains("must be a .sln, .slnx, .csproj or directory", (await Run("doctor", "--root", dir.Path, "--test", "notes.txt")).Err);
        dir.Write("Empty.slnx", "<Solution />");
        dir.Write("Empty.sln", "Microsoft Visual Studio Solution File, Format Version 12.00\n");
        Assert.Contains("No test project found", (await Run("doctor", "--root", dir.Path, "--test", "Empty.slnx")).Out);
        Assert.Contains("No test project found", (await Run("doctor", "--root", dir.Path, "--test", "Empty.sln")).Out);
    }

    [Fact]
    public async Task InputErrorsKeepTheReportContract()
    {
        using var dir = Repository();
        dir.Write("codemetr-cover.json", """{ "rules": { "unknown": 1 } }""");
        var (exit, output, error) = await Run(["check", .. Reports(dir)]);
        Assert.Equal(2, exit);
        Assert.Equal("invalidConfig", JsonDocument.Parse(output).RootElement.GetProperty("inputErrors")[0].GetProperty("code").GetString());
        Assert.Contains("[invalidConfig]", error);
        Assert.Equal(2, (await Run("check", "--root", Path.Combine(dir.Path, "missing"))).Exit);
        Assert.Equal(0, (await Run("--version")).Exit);
    }

    [Fact]
    public void FailuresAreDescribedAsInputOrToolErrors()
    {
        Assert.Equal("staleBuild", Program.Describe(new CoverInputException("staleBuild", "old")).Code);
        Assert.Equal("staleBuild", Program.Describe(new AggregateException(new CoverInputException("staleBuild", "old"))).Code);
        Assert.Equal("invalidConfig", Program.Describe(new InvalidDataException("bad")).Code);
        Assert.Equal("usage", Program.Describe(new FileNotFoundException("gone")).Code);
        var crash = Program.Describe(new InvalidOperationException("boom"));
        Assert.Equal("toolError", crash.Code);
        Assert.Equal("unexpected InvalidOperationException: boom", crash.Message);
    }
}
