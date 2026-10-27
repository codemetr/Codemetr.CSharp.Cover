namespace Codemetr.CSharp.Cover.Tests;

public sealed class GuardTests
{
    const string Code = """
        namespace Shop;

        public class Orders
        {
            public int Refund(int a, int b)
            {
                if (a > b) return 1;
                if (a == b) return 2;
                if (a < 0) return 3;
                if (b < 0) return 4;
                if (b == 0) return 5;
                return 0;
            }

            public int Simple() => 1;
        }
        """;

    static readonly int[] RefundLines = Cov.LinesWith(Code, "return").Where(l => l < Cov.LinesWith(Code, "Simple")[0]).ToArray();
    static readonly int SimpleLine = Cov.LinesWith(Code, "=> 1")[0];

    static CoverageModel Model(bool refundCovered, bool simpleCovered)
    {
        var data = new CoverageData();
        foreach (var line in RefundLines) data.Add("src/Orders.cs", line, new LineHit(refundCovered ? 1 : 0, 0, 0));
        data.Add("src/Orders.cs", SimpleLine, new LineHit(simpleCovered ? 1 : 0, 0, 0));
        return Cov.Model("src/Orders.cs", Code, data);
    }

    static GuardInput Input(CoverageModel model, CoverBaseline? baseline = null, CoverConfig? config = null, bool complete = true) => new()
    {
        Model = model, Config = config ?? new CoverConfig(), Complete = complete, Baseline = baseline,
        Exclusions = model.Excluded.Select(e => new ExclusionEntry(ExclusionEntry.Attribute, e.Type, e.Member, e.Reason)).ToArray(),
    };

    [Fact]
    public void CrapAboveTheLimitIsANewFindingWithDetails()
    {
        var result = CoverGuard.Evaluate(Input(Model(refundCovered: false, simpleCovered: true)));
        var crap = Assert.Single(result.Findings);
        Assert.Equal(CoverRuleIds.CrapScore, crap.Rule);
        Assert.Equal(Finding.New, crap.Status);
        Assert.Equal("Refund(int, int)", crap.Member);
        Assert.Equal(42, crap.Value); // CC 6, 0 % covered: 36 + 6
        Assert.NotNull(crap.UncoveredLines);
        Assert.False(result.Passed);
    }

    [Fact]
    public void RecordedDebtIsBaselinedAndAFixMakesItStale()
    {
        var config = new CoverConfig { Rules = new CoverRules { CrapScore = 20 } };
        var failing = Input(Model(false, true), config: config);
        var first = CoverGuard.Evaluate(failing);
        var baseline = CoverGuard.Initial(failing, first, "abc");
        Assert.Single(baseline.Debt);

        var again = CoverGuard.Evaluate(failing with { Baseline = baseline });
        Assert.Equal(Finding.Baselined, Assert.Single(again.Findings).Status);
        Assert.True(again.Passed);

        var fixedRun = Input(Model(true, true), baseline, config);
        var result = CoverGuard.Evaluate(fixedRun);
        Assert.Single(result.Stale);
        Assert.False(result.Passed);
        var (tightened, refused) = CoverGuard.Tighten(baseline, fixedRun, result, "def");
        Assert.Equal(0, refused);
        Assert.Empty(tightened.Debt);
        Assert.True(CoverGuard.Evaluate(fixedRun with { Baseline = tightened }).Passed);
    }

    [Fact]
    public void ACoverageDropBelowTheRecordedFloorFailsAndTighteningNeverLowersIt()
    {
        var covered = Input(Model(true, true));
        var baseline = CoverGuard.Initial(covered, CoverGuard.Evaluate(covered), null);
        Assert.Contains(baseline.Members, m => m.Member == "Simple()" && m.Line == 100);

        var dropped = Input(Model(true, false), baseline);
        var result = CoverGuard.Evaluate(dropped);
        var finding = Assert.Single(result.Findings, f => f.Rule == CoverRuleIds.CoverageDropped);
        Assert.Equal("Simple()", finding.Member);
        var (tightened, refused) = CoverGuard.Tighten(baseline, dropped, result, null);
        Assert.Equal(1, refused);
        Assert.Equal(100, tightened.Members.Single(m => m.Member == "Simple()").Line);
    }

    [Fact]
    public void DiffAndRunThresholdsJudgeChangedAndTotalLines()
    {
        var model = Model(false, true);
        var count = model.CountLines(new Dictionary<string, IReadOnlySet<int>> { ["src/Orders.cs"] = RefundLines.ToHashSet() }, out var uncovered);
        var config = new CoverConfig { Rules = new CoverRules { LineCoverage = 50, CrapScore = 100 } };
        var result = CoverGuard.Evaluate(Input(model, config: config) with { Diff = new DiffCoverage("origin/main", count, uncovered) });
        Assert.Equal([CoverRuleIds.DiffLineCoverage, CoverRuleIds.LineCoverage], result.Findings.Select(f => f.Rule).Order(StringComparer.Ordinal));
        // A partial run does not judge totals.
        var partial = CoverGuard.Evaluate(Input(model, config: config, complete: false));
        Assert.Empty(partial.Findings);
    }

    [Fact]
    public void ANewExclusionAndALoosenedThresholdAreFindings()
    {
        var input = Input(Model(true, true));
        var baseline = CoverGuard.Initial(input, CoverGuard.Evaluate(input), null);
        var excluded = input with
        {
            Baseline = baseline,
            Config = new CoverConfig { Rules = new CoverRules { DiffLineCoverage = 50 } },
            Exclusions = [new ExclusionEntry(ExclusionEntry.Glob, null, null, "src/Orders/**")],
        };
        var rules = CoverGuard.Evaluate(excluded).Findings.Select(f => f.Rule).ToArray();
        Assert.Contains(CoverRuleIds.NewCoverageExclusion, rules);
        Assert.Contains(CoverRuleIds.ThresholdLowered, rules);
    }

    [Fact]
    public void FailedAndAdditionallySkippedTestsFail()
    {
        var input = Input(Model(true, true)) with { Tests = [new ProjectTests("tests/A.csproj", new TestCounts(3, 3, 0, 1))] };
        var baseline = CoverGuard.Initial(input, CoverGuard.Evaluate(input), null);
        var worse = input with { Baseline = baseline, Tests = [new ProjectTests("tests/A.csproj", new TestCounts(4, 1, 1, 2))] };
        var rules = CoverGuard.Evaluate(worse).Findings.Select(f => f.Rule).ToArray();
        Assert.Contains(CoverRuleIds.TestsFailed, rules);
        Assert.Contains(CoverRuleIds.SkippedTestsIncreased, rules);
    }

    [Fact]
    public void AMemberThatMovedIntoUnmeasuredCodeIsNotSilentlyDropped()
    {
        var input = Input(Model(true, true));
        var baseline = CoverGuard.Initial(input, CoverGuard.Evaluate(input), null);
        var empty = CoverageModel.Build([], new CoverageData(), UnreportedMode.Zero);
        var hidden = CoverGuard.Evaluate(Input(empty, baseline) with { DeclaredInUnmeasuredCode = _ => true });
        Assert.Contains(hidden.Findings, f => f.Rule == CoverRuleIds.NoLongerMeasured);
        var removed = CoverGuard.Evaluate(Input(empty, baseline));
        Assert.True(removed.Passed);
        Assert.NotEmpty(removed.RemovedMembers);
    }

    [Fact]
    public void BaselineRoundTripsThroughItsFile()
    {
        using var dir = new TempDirectory();
        var input = Input(Model(false, true));
        var baseline = CoverGuard.Initial(input, CoverGuard.Evaluate(input), "abc");
        var path = Path.Combine(dir.Path, CoverBaseline.FileName);
        baseline.Save(path);
        Assert.DoesNotContain("\"key\"", File.ReadAllText(path));
        var loaded = CoverBaseline.Load(path);
        Assert.Equal(baseline.Members, loaded.Members);
        Assert.Equal(baseline.Debt, loaded.Debt);
        Assert.Equal(30, loaded.RecordedAt.Thresholds[CoverRuleIds.CrapScore]);
    }

    [Fact]
    public void ConfigurationIsStrict()
    {
        using var dir = new TempDirectory();
        Assert.Throws<InvalidDataException>(() => CoverConfig.Load(dir.Write("a.json", """{ "rules": { "lineCoverag": 1 } }""")));
        Assert.Throws<InvalidDataException>(() => CoverConfig.Load(dir.Write("b.json", """{ "unreported": "ignore" }""")));
        Assert.Throws<InvalidDataException>(() => CoverConfig.Load(dir.Write("c.json", """{ "rules": { "lineCoverage": 120 } }""")));
        var config = CoverConfig.Load(dir.Write("d.json", """
            { // comments allowed
              "rules": { "crapScore": 20 },
              "overrides": [ { "files": "src/Legacy/**", "rules": { "crapScore": 60 } } ]
            }
            """));
        Assert.Equal(60, config.RulesFor("src/Legacy/A.cs", "A").CrapScore);
        Assert.Equal(20, config.RulesFor("src/A.cs", "A").CrapScore);
        Assert.Equal(80, config.Effective.DiffLineCoverage);
    }
}
