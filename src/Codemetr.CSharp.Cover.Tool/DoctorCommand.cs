using System.Text.Json;
using Codemetr.CSharp.Cover;

namespace Codemetr.CSharp.Cover.Tool;

/// <summary>doctor: lists test projects with platform, framework and Coverlet engine, and the exact fix for each problem.</summary>
internal static class DoctorCommand
{
    public static async Task<int> RunAsync(CliOptions options, TextWriter stdout, JsonSerializerOptions json)
    {
        var root = Path.GetFullPath(options.Request.Root);
        if (!Directory.Exists(root)) throw new CoverInputException(InputErrorCodes.Usage, $"Root not found: {options.Request.Root}");
        var config = File.Exists(Path.Combine(root, CoverConfig.FileName)) ? CoverConfig.Load(Path.Combine(root, CoverConfig.FileName)) : new CoverConfig();
        var projects = TestProjects.Discover(root, options.Request.TestTarget ?? config.Test?.Target,
            options.Request.Configuration ?? config.Test?.Configuration ?? "Debug", config.Exclude, CancellationToken.None);
        var errors = projects.SelectMany(p => p.Problems().Select(problem => new InputError(problem.Code, problem.Message, problem.Fix))).ToList();
        if (projects.Count == 0) errors.Add(new InputError(InputErrorCodes.NoTestProjects, "No test project found.", "Run from the repository root or pass --test."));
        var report = new DoctorReport(ReportBuilder.ReportSchema, "codemetr-csharp-cover", CoverService.Version, CliOptions.Doctor, errors.Count == 0,
            projects.Select(p => new DoctorProject(p.Path, p.Platform, p.Frameworks.ToArray(), p.RequiredEngine, p.Engine,
                p.Problems().Select(problem => problem.Fix).FirstOrDefault())).ToArray(), errors, []);
        if (options.Format == CliOptions.Json) await stdout.WriteLineAsync(JsonSerializer.Serialize(report, json));
        else TextReport.WriteDoctor(report, stdout);
        return report.Passed ? ExitCodes.Passed : ExitCodes.InputError;
    }
}
