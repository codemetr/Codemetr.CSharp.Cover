using Codemetr.CSharp;

namespace Codemetr.CSharp.Cover.Tests;

/// <summary>A temporary directory that is deleted after the test.</summary>
internal sealed class TempDirectory : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "codemetr-cover-" + Guid.NewGuid().ToString("N"));

    public TempDirectory() => Directory.CreateDirectory(Path);

    public string Write(string relative, string content)
    {
        var full = System.IO.Path.Combine(Path, relative);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
        return full;
    }

    public void Dispose()
    {
        try { Directory.Delete(Path, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}

/// <summary>Builders for coverage data and models.</summary>
internal static class Cov
{
    public static CoverageData Data(string file, params (int Line, int Hits, int Covered, int Branches)[] lines)
    {
        var data = new CoverageData();
        foreach (var (line, hits, covered, branches) in lines) data.Add(file, line, new LineHit(hits, covered, branches));
        return data;
    }

    public static CoverageData Lines(string file, IEnumerable<int> covered, IEnumerable<int> uncovered)
    {
        var data = new CoverageData();
        foreach (var line in covered) data.Add(file, line, new LineHit(1, 0, 0));
        foreach (var line in uncovered) data.Add(file, line, new LineHit(0, 0, 0));
        return data;
    }

    public static IReadOnlyList<SourceFile> Sources(params (string Path, string Text)[] documents) =>
        SourceModel.Build(documents.Select(d => new SourceDocument(d.Path, d.Text)).ToArray()).Files;

    public static CoverageModel Model(string path, string text, CoverageData data, string unreported = UnreportedMode.Zero) =>
        CoverageModel.Build(Sources((path, text)), data, unreported);

    /// <summary>1-based line numbers of the lines that contain <paramref name="marker"/>.</summary>
    public static int[] LinesWith(string text, string marker) =>
        text.Split('\n').Select((line, index) => (line, index)).Where(x => x.line.Contains(marker, StringComparison.Ordinal)).Select(x => x.index + 1).ToArray();
}
