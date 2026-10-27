using System.Security.Cryptography;

namespace Codemetr.CSharp.Cover.Tool;

/// <summary>Coverage and test results of one test project.</summary>
internal sealed record ProjectRun(TestProject Project, IReadOnlyList<string> Reports, IReadOnlyList<string> Results, Codemetr.CSharp.Cover.TestCounts Counts);

/// <summary>Builds and runs test projects with Coverlet: coverlet.collector under VSTest, coverlet.MTP under Microsoft.Testing.Platform.</summary>
internal static class TestRunner
{
    /// <summary>Oldest coverlet.MTP major version that keeps compiler-generated code.</summary>
    public const int MinimumMtpMajor = 10;

    public static IReadOnlyList<ProjectRun> Run(string root, IReadOnlyList<TestProject> projects, CoverRequest request, string configuration,
        IReadOnlyList<string> testArgs, CancellationToken cancellationToken)
    {
        var problems = projects.SelectMany(p => p.Problems()).ToArray();
        if (problems.Length > 0)
            throw new CoverInputException(problems[0].Code, string.Join("\n", problems.Select(p => p.Message + " fix: " + p.Fix)),
                "Add the Coverlet package named in each fix (the tool never edits projects), or pass --reports with existing coverage files.");

        if (request.NoBuild) CheckFreshBuild(root, projects);
        else Build(root, request.TestTarget, projects, configuration, cancellationToken);

        var runDirectory = Path.Combine(root, "TestResults", "codemetr-cover", DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff", System.Globalization.CultureInfo.InvariantCulture));
        var runs = new List<ProjectRun>();
        foreach (var project in projects)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = Path.Combine(runDirectory, project.AssemblyName);
            Directory.CreateDirectory(directory);
            var result = project.Platform == TestProject.Mtp
                ? RunMtp(root, project, directory, testArgs, cancellationToken)
                : RunVSTest(root, project, directory, configuration, testArgs, cancellationToken);
            File.WriteAllText(Path.Combine(directory, "codemetr-cover-test.log"), result.Combined);
            runs.Add(Collect(root, project, directory, result));
        }
        return runs;
    }

    static void Build(string root, string? target, IReadOnlyList<TestProject> projects, string configuration, CancellationToken cancellationToken)
    {
        IEnumerable<string> targets = target is not null && !Directory.Exists(Path.GetFullPath(target, root)) ? [target] : projects.Select(p => p.Path);
        foreach (var item in targets)
        {
            var result = ProcessRunner.Run("dotnet", ["build", item, "-c", configuration, "-nologo", "-v", "quiet"], root, cancellationToken);
            if (result.ExitCode != 0)
                throw new CoverInputException(InputErrorCodes.BuildFailed, $"Build of {item} failed:\n{result.Tail(10)}", "Fix the build; coverage of code that does not build cannot be measured.");
        }
    }

    // Coverage of old binaries describes old code: every source of a test project and its references must be older than its output.
    static void CheckFreshBuild(string root, IReadOnlyList<TestProject> projects)
    {
        foreach (var project in projects)
        {
            if (!File.Exists(project.TargetPath))
                throw new CoverInputException(InputErrorCodes.StaleBuild, $"{project.Path}: --no-build, but {project.TargetPath} does not exist.", "Build first or drop --no-build.");
            var built = File.GetLastWriteTimeUtc(project.TargetPath);
            var directories = project.ProjectReferences.Select(p => Path.GetDirectoryName(Path.GetFullPath(p, root))!).Append(Path.GetFullPath(project.Directory.Length == 0 ? "." : project.Directory, root));
            foreach (var directory in directories.Distinct(StringComparer.Ordinal))
            {
                var newest = Codemetr.CSharp.SourceCollector.EveryCSharpFile(directory)
                    .Concat(Directory.EnumerateFiles(directory, "*.csproj"))
                    .Where(f => !Codemetr.CSharp.SourceCollector.IsInSkippedDirectory(Path.GetRelativePath(directory, f).Replace('\\', '/')))
                    .Select(f => (File: f, Time: File.GetLastWriteTimeUtc(f))).OrderByDescending(f => f.Time).FirstOrDefault();
                if (newest.File is not null && newest.Time > built)
                    throw new CoverInputException(InputErrorCodes.StaleBuild,
                        $"{project.Path}: --no-build, but {TestProjects.Relative(root, newest.File)} is newer than {Path.GetFileName(project.TargetPath)}.",
                        "Build first or drop --no-build; coverage of old binaries describes old code.");
            }
        }
    }

    static ProcessResult RunVSTest(string root, TestProject project, string directory, string configuration, IReadOnlyList<string> testArgs, CancellationToken cancellationToken) =>
        ProcessRunner.Run("dotnet", ["test", project.Path, "--no-build", "-c", configuration, "--nologo",
            "--collect", "XPlat Code Coverage", "--logger", "trx", "--results-directory", directory, .. testArgs], root, cancellationToken);

    static ProcessResult RunMtp(string root, TestProject project, string directory, IReadOnlyList<string> testArgs, CancellationToken cancellationToken)
    {
        var help = ProcessRunner.Run("dotnet", ["exec", project.TargetPath, "--help"], root, cancellationToken).Combined;
        if (!help.Contains("--coverlet", StringComparison.Ordinal))
            throw new CoverInputException(InputErrorCodes.CoverletMissing, $"{project.Path}: the test application does not offer --coverlet.",
                $"dotnet add {project.Path} package {TestProject.CoverletMtp}");
        // coverlet.MTP before 10 drops compiler-generated code (async and iterator state machines, local functions): such
        // members would show 0 % although tests ran them.
        var engine = Path.Combine(Path.GetDirectoryName(project.TargetPath)!, "coverlet.MTP.dll");
        if (File.Exists(engine) && System.Diagnostics.FileVersionInfo.GetVersionInfo(engine).FileMajorPart is > 0 and < MinimumMtpMajor)
            throw new CoverInputException(InputErrorCodes.WrongCoverageEngine,
                $"{project.Path}: coverlet.MTP {System.Diagnostics.FileVersionInfo.GetVersionInfo(engine).FileVersion} misses async methods, iterators and local functions.",
                $"dotnet add {project.Path} package {TestProject.CoverletMtp} --version 10.1.0");
        var trx = help.Contains("--report-xunit-trx", StringComparison.Ordinal) ? "--report-xunit-trx"
            : help.Contains("--report-trx", StringComparison.Ordinal) ? "--report-trx"
            : throw new CoverInputException(InputErrorCodes.MissingTestResults, $"{project.Path}: the test application cannot write TRX results.",
                $"dotnet add {project.Path} package Microsoft.Testing.Extensions.TrxReport");
        // Instrumenting framework assemblies without sources can fail and silently produce no coverage; only code with sources is measured anyway.
        return ProcessRunner.Run("dotnet", ["exec", project.TargetPath, "--coverlet", "--coverlet-output-format", "cobertura",
            "--coverlet-exclude-assemblies-without-sources", "MissingAll", "--results-directory", directory, trx, .. testArgs], root, cancellationToken);
    }

    static ProjectRun Collect(string root, TestProject project, string directory, ProcessResult result)
    {
        var results = Directory.EnumerateFiles(directory, "*.trx", SearchOption.AllDirectories).Order(StringComparer.Ordinal).ToArray();
        var reports = Distinct(Directory.EnumerateFiles(directory, "*.xml", SearchOption.AllDirectories)
            .Where(f => Path.GetFileName(f).Contains("cobertura", StringComparison.OrdinalIgnoreCase))).ToArray();
        if (results.Length == 0)
            throw new CoverInputException(InputErrorCodes.TestRunCrashed, $"{project.Path}: the test run produced no results (exit {result.ExitCode}):\n{result.Tail(10)}",
                $"See {TestProjects.Relative(root, Path.Combine(directory, "codemetr-cover-test.log"))}.");
        var counts = results.Select(Codemetr.CSharp.Cover.TestCounts.ReadTrx).Aggregate(Codemetr.CSharp.Cover.TestCounts.None, (a, b) => a + b);
        if (counts.Total == 0)
            throw new CoverInputException(InputErrorCodes.NoTestsExecuted, $"{project.Path}: no tests ran.", "Check test filters; a project without executed tests proves no coverage.");
        if (reports.Length == 0)
            throw new CoverInputException(InputErrorCodes.NoCoverageProduced, $"{project.Path}: tests ran, but Coverlet wrote no coverage file:\n{result.Tail(10)}",
                $"See {TestProjects.Relative(root, Path.Combine(directory, "codemetr-cover-test.log"))}; run the test application with --diagnostic for Coverlet's own log.");
        return new ProjectRun(project, reports.Select(r => TestProjects.Relative(root, r)).ToArray(), results.Select(r => TestProjects.Relative(root, r)).ToArray(), counts);
    }

    // VSTest copies each attachment once more below the results folder; identical files are one report.
    public static IEnumerable<string> Distinct(IEnumerable<string> files)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in files.Order(StringComparer.Ordinal))
            if (seen.Add(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))))) yield return file;
    }
}
