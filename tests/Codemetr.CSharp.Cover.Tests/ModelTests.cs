namespace Codemetr.CSharp.Cover.Tests;

public sealed class ModelTests
{
    const string Calc = """
        namespace Shop;

        public class Calc
        {
            public int Abs(int a)
            {
                if (a < 0)
                    return -a;
                return a;
            }

            public int Local(int a)
            {
                return Twice(a);
                int Twice(int x) => x * 2;
            }

            [System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
            public int Hidden() => 42;

            public int Total => 1;
        }
        """;

    [Fact]
    public void MembersCarryCodemetrIdentityAndComplexity()
    {
        var file = Assert.Single(Cov.Sources(("src/Calc.cs", Calc)));
        Assert.Equal(["Abs(int)", "Local(int)", "Local(int).Twice(int)", "Hidden()", "Total.get"], file.Members.Select(m => m.Name));
        Assert.Equal(2, file.Members[0].Complexity);
        Assert.Equal("ExcludeFromCodeCoverage", file.Members.Single(m => m.Name == "Hidden()").ExcludedBy);
        // Lines of a local function belong to it, not to the enclosing method.
        var twice = Cov.LinesWith(Calc, "int Twice")[0];
        Assert.Equal("Local(int).Twice(int)", file.MemberAt(twice)!.Name);
        Assert.DoesNotContain(twice, file.Members.Single(m => m.Name == "Local(int)").CodeLines);
        Assert.Equal("if (a < 0)", file.Conditions[Cov.LinesWith(Calc, "if (a < 0)")[0]]);
    }

    [Fact]
    public void ReportedLinesAreAttributedToTheInnermostMember()
    {
        var ifLine = Cov.LinesWith(Calc, "if (a < 0)")[0];
        var data = Cov.Data("src/Calc.cs", (ifLine - 1, 1, 0, 0), (ifLine, 1, 1, 2), (ifLine + 1, 0, 0, 0), (ifLine + 2, 1, 0, 0), (ifLine + 3, 1, 0, 0));
        var model = Cov.Model("src/Calc.cs", Calc, data);
        var abs = model.Members.Single(m => m.Member.Name == "Abs(int)");
        Assert.True(abs.Reported);
        Assert.Equal(new CoverageCount(4, 5, 1, 2), abs.Count);
        Assert.Equal([ifLine + 1], abs.UncoveredLines);
        Assert.Equal("if (a < 0)", Assert.Single(abs.BranchGaps).Code);
        Assert.Equal(80.0, abs.Count.LinePercent);
        Assert.Equal(50.0, abs.Count.BranchPercent);
    }

    [Fact]
    public void UnreportedCodeCountsAsZeroByDefaultAndCanBeListedInstead()
    {
        var zero = Cov.Model("src/Calc.cs", Calc, new CoverageData());
        Assert.All(zero.Members, m => Assert.False(m.Reported));
        Assert.All(zero.Members, m => Assert.Equal(0, m.Count.CoveredLines));
        Assert.Equal(2 - 1, zero.Members.Single(m => m.Member.Name == "Abs(int)").Count.Branches);
        Assert.Equal(0.0, zero.Total.LinePercent);
        Assert.DoesNotContain(zero.Members, m => m.Member.Name == "Hidden()");
        Assert.Contains(zero.Excluded, e => e.Member == "Hidden()");

        var listed = Cov.Model("src/Calc.cs", Calc, new CoverageData(), UnreportedMode.Report);
        Assert.Empty(listed.Members);
        Assert.Equal(0, listed.Total.CoverableLines);
        Assert.NotEmpty(listed.Unreported);
    }

    [Fact]
    public void AMemberMissingFromAReportedFileIsUnreported()
    {
        var ifLine = Cov.LinesWith(Calc, "if (a < 0)")[0];
        var model = Cov.Model("src/Calc.cs", Calc, Cov.Lines("src/Calc.cs", [ifLine], []));
        var total = model.Members.Single(m => m.Member.Name == "Total.get");
        Assert.False(total.Reported);
        Assert.Equal(0.0, total.Count.LinePercent);
        Assert.Contains(model.Unreported, u => u.Member == "Total.get" && u.Reason == "member has no coverage data");
    }

    [Fact]
    public void LocalFunctionsWithoutDataInsideAReportedMemberAreNotCountedAsUntested()
    {
        var local = Cov.LinesWith(Calc, "return Twice(a)")[0];
        var model = Cov.Model("src/Calc.cs", Calc, Cov.Lines("src/Calc.cs", [local], []));
        Assert.DoesNotContain(model.Members, m => m.Member.Name == "Local(int).Twice(int)");
        Assert.Contains(model.Uninstrumented, u => u.Member == "Local(int).Twice(int)");
    }

    [Theory]
    [InlineData(1, 1.0, 1.0)]
    [InlineData(5, 0.0, 30.0)]
    [InlineData(5, 0.5, 8.1)]
    [InlineData(10, 1.0, 10.0)]
    public void CrapCombinesComplexityAndMissingCoverage(int complexity, double ratio, double expected)
    {
        var member = new SourceMember("T", "M()", "f.cs", 1, 2, complexity, [], null);
        var covered = (int)(ratio * 10);
        Assert.Equal(expected, new MemberCoverage(member, true, new CoverageCount(covered, 10, 0, 0), [], []).Crap);
    }

    [Fact]
    public void PercentagesRoundDownSoAFailingValueNeverShowsAsPassing()
    {
        Assert.Equal(79.9, new CoverageCount(7999, 10000, 0, 0).LinePercent);
        Assert.Null(new CoverageCount(1, 1, 0, 0).BranchPercent);
        Assert.Null(CoverageCount.Zero.LinePercent);
    }

    [Fact]
    public void ChangedLinesCountOnlyWhereCodeIsCoverable()
    {
        var ifLine = Cov.LinesWith(Calc, "if (a < 0)")[0];
        var model = Cov.Model("src/Calc.cs", Calc, Cov.Lines("src/Calc.cs", [ifLine], [ifLine + 1]));
        var count = model.CountLines(new Dictionary<string, IReadOnlySet<int>> { ["src/Calc.cs"] = new HashSet<int> { 1, ifLine, ifLine + 1 } }, out var uncovered);
        Assert.Equal(1, count.CoveredLines);
        Assert.Equal(2, count.CoverableLines);
        Assert.Equal("Abs(int)", Assert.Single(uncovered).Member!.Name);
    }

    [Fact]
    public void WorklistListsEverythingBelowTheThresholdsWithItsGap()
    {
        var ifLine = Cov.LinesWith(Calc, "if (a < 0)")[0];
        var data = Cov.Data("src/Calc.cs", (ifLine - 1, 1, 0, 0), (ifLine, 1, 1, 2), (ifLine + 1, 0, 0, 0), (ifLine + 2, 1, 0, 0), (ifLine + 3, 1, 0, 0));
        var model = Cov.Model("src/Calc.cs", Calc, data);
        var items = Worklist.Build(model, 90, 60, "member", "gap");
        var abs = items.Single(i => i.Member == "Abs(int)");
        Assert.Equal(["line", "branch"], abs.Below);
        Assert.Equal(new Gap(1, 1), abs.Gap);
        Assert.Contains(items, i => i.Member == "Total.get" && !i.Reported);
        Assert.Equal(items.OrderByDescending(i => i.Gap.Lines + i.Gap.Branches).Select(i => i.Gap.Lines + i.Gap.Branches), items.Select(i => i.Gap.Lines + i.Gap.Branches));
        Assert.Empty(Worklist.Build(model, 0, null, "file", "path"));
        Assert.Single(Worklist.Build(model, 90, null, "file", "path"));
    }
}
