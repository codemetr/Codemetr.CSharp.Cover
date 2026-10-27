using System.Text.RegularExpressions;
using System.Xml.Linq;
using Codemetr.CSharp.Cover;
using Microsoft.Extensions.FileSystemGlobbing;

namespace Codemetr.CSharp.Cover.Tool;

/// <summary>Coverage as obtained for a run, before it meets the sources.</summary>
internal sealed record Evidence(string Source, IReadOnlyList<(string Report, CoverageData Data)> Reports, IReadOnlyList<ProjectReport> Projects,
    IReadOnlyList<ProjectTests> Tests);

/// <summary>Obtains coverage and test results: by running the tests, or from existing report files, which must be trustworthy.</summary>
internal static class EvidenceCollector
{
    public static Evidence Collect(RunContext context, IReadOnlyList<TestProject> all, IReadOnlyList<TestProject> selected, CancellationToken cancellationToken)
    {
        context.Warnings.AddRange(CoverletFilterWarnings(context.Root, selected));
        return context.Request.Reports.Count > 0 ? FromReports(context, all) : FromRun(context, selected, cancellationToken);
    }

    static Evidence FromRun(RunContext context, IReadOnlyList<TestProject> projects, CancellationToken cancellationToken)
    {
        if (projects.Count == 0)
            throw new CoverInputException(InputErrorCodes.NoTestProjects, "No test project found" + (context.Target is null ? " below the root." : $" in {context.Target}."),
                "Run from the repository root, pass --test, or pass --reports with existing coverage files.");
        var runs = TestRunner.Run(context.Root, projects, context.Request, context.Configuration, context.TestArgs, cancellationToken);
        var mapper = Mapper(context);
        var reports = runs.SelectMany(run => run.Reports).Select(report => (report, MapReport(context, report, mapper))).ToArray();
        return new Evidence("run", reports,
            runs.Select(r => new ProjectReport(r.Project.Path, r.Project.Platform, r.Project.Engine, r.Reports, r.Counts)).ToArray(),
            runs.Select(r => new ProjectTests(r.Project.Path, r.Counts)).ToArray());
    }

    static Evidence FromReports(RunContext context, IReadOnlyList<TestProject> projects)
    {
        var mapper = Mapper(context);
        var reports = TestRunner.Distinct(Expand(context.Root, context.Request.Reports, "--reports"))
            .Select(f => (TestProjects.Relative(context.Root, f), MapReport(context, f, mapper))).ToArray();
        var results = context.Request.TestResults.Count > 0 ? Expand(context.Root, context.Request.TestResults, "--test-results").ToArray() : [];
        if (results.Length == 0)
        {
            if (context.Verdict)
                throw new CoverInputException(InputErrorCodes.MissingTestResults, "Coverage reports without test results: failed or skipped tests would be invisible.",
                    "Pass --test-results with the TRX files of the same run (VSTest: --logger trx, Microsoft.Testing.Platform: --report-trx).");
            context.Warnings.Add("No test results given (--test-results): failed or skipped tests are not visible in this report.");
            context.Partial.Add("no test results");
        }
        var (projectReports, tests) = AttributeResults(context, projects, results);
        return new Evidence("reports", reports, projectReports, tests);
    }

    // Results belong to the test project with the same assembly name; results of unknown assemblies still count.
    static (List<ProjectReport>, List<ProjectTests>) AttributeResults(RunContext context, IReadOnlyList<TestProject> projects, string[] results)
    {
        var byAssembly = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var result in results)
        {
            var assemblies = TrxAssemblies(result).ToArray();
            foreach (var assembly in assemblies.Length > 0 ? assemblies : [Path.GetFileNameWithoutExtension(result)])
                (byAssembly.TryGetValue(assembly, out var list) ? list : byAssembly[assembly] = []).Add(result);
        }
        var projectReports = new List<ProjectReport>();
        var tests = new List<ProjectTests>();
        void Add(string name, TestProject? project, List<string> trx)
        {
            var counts = ReadResults(context, name, trx);
            tests.Add(new ProjectTests(name, counts));
            projectReports.Add(new ProjectReport(name, project?.Platform, project?.Engine, trx.Select(t => TestProjects.Relative(context.Root, t)).ToArray(), counts));
        }
        foreach (var project in projects)
        {
            if (byAssembly.Remove(project.AssemblyName, out var trx)) { Add(project.Path, project, trx); continue; }
            if (results.Length == 0) continue;
            if (context.Verdict)
                throw new CoverInputException(InputErrorCodes.MissingTestProject, $"{project.Path}: no test results among --test-results.",
                    "Every test project must run in the measured run; pass its TRX file, or narrow with --test when running tests.");
            context.Partial.Add($"{project.Path} has no test results");
        }
        foreach (var (assembly, trx) in byAssembly.OrderBy(pair => pair.Key, StringComparer.Ordinal)) Add(assembly, null, trx);
        return (projectReports, tests);
    }

    static TestCounts ReadResults(RunContext context, string name, List<string> trx)
    {
        if (trx.Count > 1)
        {
            var message = $"{name}: {trx.Count} result files ({string.Join(", ", trx.Select(t => TestProjects.Relative(context.Root, t)))}); old runs may be mixed in.";
            if (context.Verdict) throw new CoverInputException(InputErrorCodes.DuplicateReport, message, "Pass the results of exactly one run, for example a fresh --results-directory.");
            context.Warnings.Add(message);
        }
        var counts = trx.Select(TestCounts.ReadTrx).Aggregate(TestCounts.None, (a, b) => a + b);
        if (counts.Total == 0 && context.Verdict)
            throw new CoverInputException(InputErrorCodes.NoTestsExecuted, $"{name}: no tests ran.", "A project without executed tests proves no coverage.");
        return counts;
    }

    // Deterministic builds (ContinuousIntegrationBuild) write sources as /_/...; that is the repository root.
    static PathMapper Mapper(RunContext context) =>
        new(context.Root, context.Request.PathMaps.Append(("/_/", PathMapper.Normalize(context.Root).TrimEnd('/') + "/")));

    static CoverageData MapReport(RunContext context, string report, PathMapper mapper)
    {
        var (data, _) = CoverageReportReader.Read(Path.GetFullPath(report, context.Root));
        var (mapped, foreign) = mapper.Map(data);
        var foreignSources = foreign.Where(f => f.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (foreignSources.Length == 0) return mapped;
        var message = $"{TestProjects.Relative(context.Root, Path.GetFullPath(report, context.Root))}: {foreignSources.Length} source path(s) outside the root, e.g. {foreignSources[0]}";
        if (context.Verdict)
            throw new CoverInputException(InputErrorCodes.ForeignCoverage, message, "The report comes from another checkout or machine: pass --path-map FROM=TO or the right --root.");
        context.Warnings.Add(message);
        return mapped;
    }

    /// <summary>A report older than a source it covers describes old code; so does one that names lines past the end of a file.</summary>
    public static void CheckFreshness(RunContext context, Evidence evidence, IReadOnlyList<SourceFile> sources)
    {
        if (evidence.Source != "reports") return;
        var byPath = sources.ToDictionary(s => s.Path, StringComparer.Ordinal);
        foreach (var (report, data) in evidence.Reports)
        {
            var written = File.GetLastWriteTimeUtc(Path.GetFullPath(report, context.Root));
            var stale = data.Files.Where(byPath.ContainsKey)
                .Select(file => (File: file, PastEnd: data.Lines(file)!.Keys.Any(line => line > byPath[file].Text.Length)))
                .FirstOrDefault(f => f.PastEnd || File.GetLastWriteTimeUtc(Path.Combine(context.Root, f.File)) > written);
            if (stale.File is null) continue;
            var message = $"{report} is older than {stale.File}{(stale.PastEnd ? " (it names lines past the end of the file)" : "")}: the coverage describes old code.";
            if (context.Verdict)
                throw new CoverInputException(InputErrorCodes.StaleCoverage, message, "Run the tests again (drop --reports) or pass the reports of a run after the last change.");
            context.Warnings.Add(message);
        }
    }

    static IEnumerable<string> Expand(string root, IReadOnlyList<string> patterns, string option)
    {
        var found = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var pattern in patterns)
        {
            var full = Path.GetFullPath(pattern, root);
            if (File.Exists(full)) { found.Add(full); continue; }
            var matcher = new Matcher(StringComparison.OrdinalIgnoreCase);
            matcher.AddInclude(pattern.Replace('\\', '/'));
            var matches = matcher.GetResultsInFullPath(root).ToArray();
            if (matches.Length == 0) throw new CoverInputException(InputErrorCodes.Usage, $"{option} {pattern}: no file matches below {root}.");
            found.UnionWith(matches.Select(Path.GetFullPath));
        }
        return found;
    }

    static IEnumerable<string> TrxAssemblies(string path) =>
        XDocument.Load(path).Descendants().Where(e => e.Name.LocalName == "UnitTest").Select(e => (string?)e.Attribute("storage")).OfType<string>()
            .Select(storage => Path.GetFileNameWithoutExtension(storage.Replace('\\', '/').Split('/')[^1])).Distinct(StringComparer.OrdinalIgnoreCase);

    // Coverlet filters in project files or runsettings remove code from the report; the tool counts that code as unreported.
    static IEnumerable<string> CoverletFilterWarnings(string root, IReadOnlyList<TestProject> projects)
    {
        var pattern = new Regex(@"<(ExcludeByFile|ExcludeByAttribute|Exclude|Include)>|coverlet-(exclude|include)", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
        var files = projects.Select(p => Path.GetFullPath(p.Path, root))
            .Concat(Directory.EnumerateFiles(root, "*.runsettings", SearchOption.TopDirectoryOnly))
            .Concat(new[] { "Directory.Build.props" }.Select(f => Path.Combine(root, f)).Where(File.Exists));
        foreach (var file in files.Distinct(StringComparer.Ordinal))
            if (pattern.IsMatch(File.ReadAllText(file)))
                yield return $"{TestProjects.Relative(root, file)} configures Coverlet include/exclude filters; code they remove counts as unreported, not as excluded. "
                    + $"Use [ExcludeFromCodeCoverage] or 'exclude' in {CoverConfig.FileName} for intended exclusions.";
    }
}
