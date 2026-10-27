using Microsoft.Extensions.FileSystemGlobbing;
using System.Text.Json;
using System.Xml.Linq;

namespace Codemetr.CSharp.Cover.Tool;

/// <summary>A test project as MSBuild evaluates it.</summary>
internal sealed record TestProject(string Path, string Directory, string AssemblyName, string TargetPath, string Platform,
    IReadOnlyList<string> Packages, IReadOnlyList<string> ProjectReferences)
{
    public const string VSTest = "vstest", Mtp = "mtp";
    public const string Collector = "coverlet.collector", CoverletMtp = "coverlet.MTP";

    /// <summary>The Coverlet package the platform needs.</summary>
    public string RequiredEngine => Platform == Mtp ? CoverletMtp : Collector;

    public bool Has(string package) => Packages.Contains(package, StringComparer.OrdinalIgnoreCase);

    public string? Engine => Has(RequiredEngine) ? RequiredEngine : null;

    public IEnumerable<string> Frameworks => new[]
    {
        Packages.Any(p => p.StartsWith("xunit.v3", StringComparison.OrdinalIgnoreCase)) ? "xunit.v3"
            : Has("xunit") || Has("xunit.core") ? "xunit" : null,
        Packages.Any(p => p.StartsWith("NUnit", StringComparison.OrdinalIgnoreCase)) ? "nunit" : null,
        Packages.Any(p => p.StartsWith("MSTest", StringComparison.OrdinalIgnoreCase)) ? "mstest" : null,
        Packages.Any(p => p.StartsWith("TUnit", StringComparison.OrdinalIgnoreCase)) ? "tunit" : null,
    }.OfType<string>();

    /// <summary>Setup problems with the exact fix, or empty.</summary>
    public IEnumerable<(string Code, string Message, string Fix)> Problems()
    {
        var other = Platform == Mtp ? Collector : CoverletMtp;
        if (Has(RequiredEngine)) yield break;
        if (Has(other))
            yield return (InputErrorCodes.WrongCoverageEngine,
                $"{Path}: references {other}, but runs on {(Platform == Mtp ? "Microsoft.Testing.Platform" : "VSTest")}, which needs {RequiredEngine}.",
                $"dotnet remove {Path} package {other} && dotnet add {Path} package {RequiredEngine}");
        else
            yield return (InputErrorCodes.CoverletMissing, $"{Path}: no coverage engine (platform {Platform}).", $"dotnet add {Path} package {RequiredEngine}");
    }
}

/// <summary>Finds test projects below the root (or in a target) with MSBuild evaluation, so props files and central package management count.</summary>
internal static class TestProjects
{
    static readonly string[] Skipped = ["bin", "obj", ".git", "node_modules", "TestResults"];

    public static IReadOnlyList<TestProject> Discover(string root, string? target, string configuration, IReadOnlyCollection<string> exclude,
        CancellationToken cancellationToken)
    {
        // Projects in excluded paths (for example sample solutions used as test data) are not part of the run.
        var matcher = new Microsoft.Extensions.FileSystemGlobbing.Matcher(StringComparison.Ordinal);
        matcher.AddIncludePatterns(exclude);
        var projects = Candidates(root, target).Where(p => exclude.Count == 0 || !matcher.Match(Relative(root, p)).HasMatches).ToList();
        var results = new TestProject?[projects.Count];
        Parallel.For(0, projects.Count, new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = cancellationToken },
            index => results[index] = Evaluate(root, projects[index], configuration, cancellationToken));
        return results.OfType<TestProject>().OrderBy(p => p.Path, StringComparer.Ordinal).ToArray();
    }

    static List<string> Candidates(string root, string? target)
    {
        if (target is null) return Enumerate(root).ToList();
        var full = System.IO.Path.GetFullPath(target, root);
        if (Directory.Exists(full)) return Enumerate(full).ToList();
        if (!File.Exists(full)) throw new CoverInputException(InputErrorCodes.Usage, $"Test target not found: {target}");
        var directory = System.IO.Path.GetDirectoryName(full)!;
        return System.IO.Path.GetExtension(full).ToLowerInvariant() switch
        {
            ".csproj" => [full],
            ".slnx" => XDocument.Load(full).Descendants("Project").Select(p => (string?)p.Attribute("Path")).OfType<string>()
                .Where(p => p.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
                .Select(p => System.IO.Path.GetFullPath(p.Replace('\\', '/'), directory)).ToList(),
            ".sln" => File.ReadAllLines(full).Where(l => l.StartsWith("Project(", StringComparison.Ordinal))
                .Select(l => l.Split(',').ElementAtOrDefault(1)?.Trim().Trim('"')).OfType<string>()
                .Where(p => p.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
                .Select(p => System.IO.Path.GetFullPath(p.Replace('\\', '/'), directory)).ToList(),
            _ => throw new CoverInputException(InputErrorCodes.Usage, $"Test target must be a .sln, .slnx, .csproj or directory: {target}"),
        };
    }

    static IEnumerable<string> Enumerate(string directory)
    {
        foreach (var file in Directory.EnumerateFiles(directory, "*.csproj")) yield return file;
        foreach (var child in Directory.EnumerateDirectories(directory))
        {
            if (Skipped.Contains(System.IO.Path.GetFileName(child), StringComparer.OrdinalIgnoreCase)) continue;
            if (new DirectoryInfo(child).LinkTarget is not null) continue;
            foreach (var file in Enumerate(child)) yield return file;
        }
    }

    static TestProject? Evaluate(string root, string project, string configuration, CancellationToken cancellationToken)
    {
        // Test SDK and runner packages set IsTestProject and IsTestingPlatformApplication from their props, which only exist after restore.
        if (!File.Exists(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(project)!, "obj", "project.assets.json")))
        {
            var restore = ProcessRunner.Run("dotnet", ["restore", project, "-nologo", "-v", "quiet"], root, cancellationToken);
            if (restore.ExitCode != 0)
                throw new CoverInputException(InputErrorCodes.InvalidConfig, $"Restore of {Relative(root, project)} failed: {restore.Tail(5)}");
        }
        var result = ProcessRunner.Run("dotnet", ["msbuild", project, "-nologo", $"-property:Configuration={configuration}",
            "-getProperty:IsTestProject", "-getProperty:IsTestingPlatformApplication", "-getProperty:TargetPath", "-getProperty:AssemblyName",
            "-getItem:PackageReference", "-getItem:ProjectReference"], root, cancellationToken);
        if (result.ExitCode != 0)
            throw new CoverInputException(InputErrorCodes.InvalidConfig, $"MSBuild cannot evaluate {Relative(root, project)}: {result.Tail(5)}");
        var properties0 = result.Output.IndexOf("\"Properties\"", StringComparison.Ordinal);
        if (properties0 < 0) throw new CoverInputException(InputErrorCodes.InvalidConfig, $"MSBuild returned no properties for {Relative(root, project)}");
        using var json = JsonDocument.Parse(result.Output[result.Output.LastIndexOf('{', properties0)..]);
        var properties = json.RootElement.GetProperty("Properties");
        string Property(string name) => properties.TryGetProperty(name, out var value) ? value.GetString() ?? "" : "";
        var isMtp = Property("IsTestingPlatformApplication").Equals("true", StringComparison.OrdinalIgnoreCase);
        if (!isMtp && !Property("IsTestProject").Equals("true", StringComparison.OrdinalIgnoreCase)) return null;
        var items = json.RootElement.GetProperty("Items");
        IReadOnlyList<string> Items(string name, string field) => items.TryGetProperty(name, out var list)
            ? list.EnumerateArray().Select(i => i.GetProperty(field).GetString()).OfType<string>().ToArray() : [];
        var path = Relative(root, project);
        return new TestProject(path, Relative(root, System.IO.Path.GetDirectoryName(project)!), Property("AssemblyName"), Property("TargetPath"),
            isMtp ? TestProject.Mtp : TestProject.VSTest, Items("PackageReference", "Identity"),
            Items("ProjectReference", "FullPath").Select(p => Relative(root, p)).ToArray());
    }

    public static string Relative(string root, string path) =>
        System.IO.Path.GetRelativePath(root, path).Replace('\\', '/') is var relative && relative == "." ? "" : relative;
}
