namespace Codemetr.CSharp.Cover.Tests;

public sealed class ReaderTests
{
    const string Cobertura = """
        <?xml version="1.0" encoding="utf-8"?>
        <coverage line-rate="0.5" branch-rate="0.5" version="1.9" timestamp="1">
          <sources><source>C:\</source></sources>
          <packages><package name="Lib"><classes>
            <class name="Shop.Calc" filename="repo\src\Calc.cs">
              <methods><method name="Abs" signature="(System.Int32)"><lines><line number="7" hits="3" branch="True" condition-coverage="50% (1/2)" /></lines></method></methods>
              <lines>
                <line number="6" hits="3" branch="False" />
                <line number="7" hits="3" branch="True" condition-coverage="50% (1/2)" />
                <line number="8" hits="0" branch="False" />
              </lines>
            </class>
            <class name="Shop.Calc/&lt;Items&gt;d__3" filename="repo\src\Calc.cs">
              <lines><line number="20" hits="1" branch="False" /></lines>
            </class>
          </classes></package></packages>
        </coverage>
        """;

    [Fact]
    public void CoberturaJoinsSourceAndFilenameAndReadsBranchCounts()
    {
        using var dir = new TempDirectory();
        var (data, format) = CoverageReportReader.Read(dir.Write("coverage.cobertura.xml", Cobertura));
        Assert.Equal(CoverageReportReader.Cobertura, format);
        var file = Assert.Single(data.Files);
        Assert.Equal(@"C:\repo\src\Calc.cs", file);
        var lines = data.Lines(file)!;
        Assert.Equal(new LineHit(3, 1, 2), lines[7]);
        Assert.Equal(0, lines[8].Hits);
        Assert.Equal(1, lines[20].Hits); // compiler-generated classes count for the same file
        Assert.Equal(4, lines.Count);
    }

    [Fact]
    public void OpenCoverCountsSequenceAndBranchPoints()
    {
        using var dir = new TempDirectory();
        var path = dir.Write("coverage.opencover.xml", """
            <CoverageSession><Modules><Module>
              <Files><File uid="1" fullPath="/repo/src/Calc.cs" /></Files>
              <Classes><Class><Methods><Method>
                <FileRef uid="1" />
                <SequencePoints>
                  <SequencePoint vc="2" sl="5" fileid="1" />
                  <SequencePoint vc="0" sl="6" fileid="1" />
                </SequencePoints>
                <BranchPoints>
                  <BranchPoint vc="1" sl="5" fileid="1" />
                  <BranchPoint vc="0" sl="5" fileid="1" />
                </BranchPoints>
              </Method></Methods></Class></Classes>
            </Module></Modules></CoverageSession>
            """);
        var (data, format) = CoverageReportReader.Read(path);
        Assert.Equal(CoverageReportReader.OpenCover, format);
        var lines = data.Lines("/repo/src/Calc.cs")!;
        Assert.Equal(new LineHit(2, 1, 2), lines[5]);
        Assert.Equal(new LineHit(0, 0, 0), lines[6]);
    }

    [Fact]
    public void UnknownOrBrokenFilesAreInputErrors()
    {
        using var dir = new TempDirectory();
        Assert.Throws<InvalidDataException>(() => CoverageReportReader.Read(dir.Write("a.xml", "<other />")));
        Assert.Throws<InvalidDataException>(() => CoverageReportReader.Read(dir.Write("b.xml", "<coverage>")));
    }

    [Fact]
    public void MergeCoversALineWhenAnyRunCoversIt()
    {
        var a = Cov.Data("f.cs", (1, 0, 0, 2), (2, 1, 0, 0));
        var b = Cov.Data("f.cs", (1, 2, 1, 2), (3, 0, 0, 0));
        a.Merge(b);
        Assert.Equal(new LineHit(2, 1, 2), a.Lines("f.cs")![1]);
        Assert.Equal(3, a.Lines("f.cs")!.Count);
    }

    [Fact]
    public void TrxCountersIncludeFailuresAndSkips()
    {
        using var dir = new TempDirectory();
        var path = dir.Write("r.trx", """
            <TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
              <ResultSummary outcome="Failed"><Counters total="5" executed="4" passed="2" failed="1" error="1" timeout="0" aborted="0" notExecuted="1" /></ResultSummary>
            </TestRun>
            """);
        Assert.Equal(new TestCounts(5, 2, 2, 1), TestCounts.ReadTrx(path));
    }
}

public sealed class PathMapperTests
{
    [Fact]
    public void AbsolutePathsBelowTheRootBecomeRelativeWithForwardSlashes()
    {
        var root = Path.Combine(Path.GetTempPath(), "repo");
        var mapper = new PathMapper(root, ignoreCase: true);
        Assert.Equal("src/A.cs", mapper.Relative(Path.Combine(root, "src", "A.cs")));
        Assert.Equal("src/A.cs", mapper.Relative(Path.Combine(root, "src", ".", "x", "..", "A.cs")));
        Assert.Null(mapper.Relative(Path.Combine(Path.GetTempPath(), "other", "A.cs")));
        Assert.Equal("src/A.cs", mapper.Relative("src/A.cs"));
    }

    [Fact]
    public void PathMapsTranslateReportsFromElsewhere()
    {
        var root = Path.Combine(Path.GetTempPath(), "repo");
        var mapper = new PathMapper(root, [("/build/agent/", PathMapper.Normalize(root) + "/")], ignoreCase: true);
        Assert.Equal("src/A.cs", mapper.Relative("/build/agent/src/A.cs"));
        Assert.Equal("src/A.cs", mapper.Relative(@"\build\agent\src\A.cs"));
    }
}

public sealed class UnifiedDiffTests
{
    [Fact]
    public void ReadsAddedLinesOfGitDiffWithoutContext()
    {
        var diff = UnifiedDiff.Parse("""
            diff --git a/src/A.cs b/src/A.cs
            index 1..2 100644
            --- a/src/A.cs
            +++ b/src/A.cs
            @@ -3,0 +4,2 @@ class A
            +    int X;
            +    int Y;
            @@ -10 +12 @@
            -old
            +new
            diff --git a/src/Gone.cs b/src/Gone.cs
            --- a/src/Gone.cs
            +++ /dev/null
            @@ -1,2 +0,0 @@
            -a
            -b
            """);
        Assert.Equal([4, 5, 12], diff["src/A.cs"]);
        Assert.Single(diff);
    }

    [Fact]
    public void HunkCountsKeepContentLinesThatLookLikeHeaders()
    {
        var diff = UnifiedDiff.Parse("""
            --- a/f.cs
            +++ b/f.cs
            @@ -1,2 +1,3 @@
             keep
            +++ added text
            --- removed text
            +x
            --- a/g.cs
            +++ b/g.cs
            @@ -0,0 +1 @@
            +y
            """);
        Assert.Equal([2, 3], diff["f.cs"]);
        Assert.Equal([1], diff["g.cs"]);
    }

    [Fact]
    public void QuotedPathsWithEscapesAreRejected() =>
        Assert.Throws<InvalidDataException>(() => UnifiedDiff.Parse("+++ \"b/src/\\303\\234.cs\"\n@@ -0,0 +1 @@\n+x\n"));
}
