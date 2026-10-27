using System.Text.Json;
using Codemetr.CSharp.Cover.Tool;

namespace Codemetr.CSharp.Cover.Tests;

/// <summary>Runs only with CODEMETR_COVER_INTEGRATION=1: it builds and tests a real solution, which needs NuGet access.</summary>
internal sealed class IntegrationFactAttribute : FactAttribute
{
    public IntegrationFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("CODEMETR_COVER_INTEGRATION") != "1")
            Skip = "Set CODEMETR_COVER_INTEGRATION=1 to build and test tests/fixtures/Sample.";
    }
}

public sealed class IntegrationTests
{
    static string Fixture([System.Runtime.CompilerServices.CallerFilePath] string here = "") =>
        Path.Combine(Path.GetDirectoryName(here)!, "..", "fixtures", "Sample");

    static void Copy(string from, string to)
    {
        foreach (var directory in Directory.EnumerateDirectories(from, "*", SearchOption.AllDirectories))
            if (!directory.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj" or "TestResults"))
                Directory.CreateDirectory(directory.Replace(from, to));
        foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
            if (!file.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj" or "TestResults"))
                File.Copy(file, file.Replace(from, to), overwrite: true);
    }

    [IntegrationFact]
    public async Task MeasuresXunitNUnitMSTestOnVSTestAndMicrosoftTestingPlatform()
    {
        using var dir = new TempDirectory();
        Copy(Path.GetFullPath(Fixture()), dir.Path);
        using var output = new StringWriter();
        using var error = new StringWriter();
        var exit = await Program.RunAsync(["analyze", "--root", dir.Path, "--output", Path.Combine(dir.Path, "full.json")], new StringReader(""), output, error);
        Assert.True(exit == 0, error.ToString() + output);
        var report = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir.Path, "full.json"))).RootElement;

        var projects = report.GetProperty("provenance").GetProperty("testProjects").EnumerateArray()
            .ToDictionary(p => p.GetProperty("project").GetString()!, p => p);
        Assert.Equal("mtp", projects["tests/MSTestT/MSTestT.csproj"].GetProperty("platform").GetString());
        Assert.Equal("mtp", projects["tests/XunitV3/XunitV3.csproj"].GetProperty("platform").GetString());
        Assert.Equal("vstest", projects["tests/NUnitT/NUnitT.csproj"].GetProperty("platform").GetString());
        Assert.Equal("vstest", projects["tests/XunitV2/XunitV2.csproj"].GetProperty("platform").GetString());
        Assert.All(projects.Values, p => Assert.Equal(1, p.GetProperty("tests").GetProperty("passed").GetInt32()));

        var members = report.GetProperty("types")[0].GetProperty("members").EnumerateArray()
            .ToDictionary(m => m.GetProperty("member").GetString()!, m => m.GetProperty("coverage"));
        double? Line(string member) => members[member].TryGetProperty("line", out var value) ? value.GetDouble() : null;
        Assert.Equal(80.0, Line("Abs(int)"));        // xUnit v2 on VSTest
        Assert.Equal(100.0, Line("LoadAsync(int)"));  // NUnit on VSTest, async state machine
        Assert.Equal(100.0, Line("Items(int)"));      // MSTest on MTP, iterator state machine
        Assert.Equal(100.0, Line("Local(int)"));      // xUnit v3 on MTP
        Assert.Equal(0.0, Line("Never(int)"));

        // The binaries are fresh: --no-build may reuse them. After a source change it must refuse.
        Assert.Equal(0, await Program.RunAsync(["analyze", "--root", dir.Path, "--no-build"], new StringReader(""), new StringWriter(), new StringWriter()));
        File.SetLastWriteTimeUtc(Path.Combine(dir.Path, "src", "Lib", "Calc.cs"), DateTime.UtcNow.AddMinutes(1));
        using var stale = new StringWriter();
        Assert.Equal(2, await Program.RunAsync(["analyze", "--root", dir.Path, "--no-build"], new StringReader(""), stale, new StringWriter()));
        Assert.Contains("staleBuild", stale.ToString());
    }
}
