using Codemetr.CSharp.Cover.Tool;

namespace Codemetr.CSharp.Cover.Tests;

public sealed class ModelWarningsTests
{
    const string Code = """
        namespace Shop;

        public class Calc
        {
            public int Local(int a)
            {
                return Twice(a);
                int Twice(int x) => x * 2;
            }
        }
        """;

    static Evidence Evidence(params string[] platforms) =>
        new("run", [], platforms.Select((p, i) => new ProjectReport($"tests/P{i}.csproj", p, null, [], null)).ToArray(), []);

    [Fact]
    public void UninstrumentedLocalFunctionsAreNamed()
    {
        var outer = Cov.LinesWith(Code, "return Twice")[0];
        var model = Cov.Model("src/Calc.cs", Code, Cov.Lines("src/Calc.cs", [outer], []));
        Assert.Contains(ModelWarnings.For(model, Evidence("mtp"), UnreportedMode.Zero), w => w.Contains("1 local function(s) have no coverage data"));
    }

    [Fact]
    public void MixedPlatformsWithUncoveredLocalFunctionsAreFlagged()
    {
        var twice = Cov.LinesWith(Code, "int Twice")[0];
        var outer = Cov.LinesWith(Code, "return Twice")[0];
        var model = Cov.Model("src/Calc.cs", Code, Cov.Lines("src/Calc.cs", [outer], [twice]));
        Assert.Contains(ModelWarnings.For(model, Evidence("mtp", "vstest"), UnreportedMode.Zero), w => w.Contains("coverlet.MTP does not report local functions"));
        Assert.Empty(ModelWarnings.For(model, Evidence("vstest"), UnreportedMode.Zero));
    }

    [Fact]
    public void UnmappedLinesAndUnreportedFilesAreFlagged()
    {
        var model = CoverageModel.Build(Cov.Sources(("src/Calc.cs", Code), ("src/Other.cs", "namespace Shop;\npublic class Other { public int M() => 1; }\n")),
            Cov.Lines("src/Calc.cs", [Cov.LinesWith(Code, "return Twice")[0], 1], []), UnreportedMode.Zero);
        var warnings = ModelWarnings.For(model, Evidence("vstest"), UnreportedMode.Zero).ToArray();
        Assert.Contains(warnings, w => w.Contains("1 covered line(s) lie outside any member"));
        Assert.Contains(warnings, w => w.Contains("1 file(s) have no coverage data and count as 0%"));
        Assert.DoesNotContain(ModelWarnings.For(model, Evidence("vstest"), UnreportedMode.Report), w => w.Contains("count as 0%"));
    }
}
