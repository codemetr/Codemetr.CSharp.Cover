using Codemetr.CSharp.Cover;

namespace Codemetr.CSharp.Cover.Tool;

/// <summary>The settled inputs of one run: request and configuration combined, plus what the run collects on the way.</summary>
internal sealed class RunContext
{
    static readonly string[] FilterOptions = ["--filter", "--treenode-filter", "--filter-class", "--filter-method", "--filter-namespace",
        "--filter-trait", "--filter-not-class", "--filter-not-method", "--filter-not-namespace", "--filter-not-trait", "--filter-query", "--where"];

    public required string Command { get; init; }
    public required CoverRequest Request { get; init; }
    public required string Root { get; init; }
    public required CoverConfig Config { get; init; }
    public string? ConfigFile { get; init; }

    /// <summary>A verdict command: untrustworthy evidence is an error, not a warning.</summary>
    public bool Verdict => Command != CliOptions.Analyze;
    public string Unreported => Request.Unreported ?? Config.Unreported;
    public string Configuration => Request.Configuration ?? Config.Test?.Configuration ?? "Debug";
    public string? Target => Request.TestTarget ?? Config.Test?.Target;
    public IReadOnlyList<string> TestArgs => Request.TestArgs.Count > 0 ? Request.TestArgs : Config.Test?.Args ?? [];
    public bool HasTestFilter => TestArgs.Any(arg => FilterOptions.Any(option => arg.StartsWith(option, StringComparison.OrdinalIgnoreCase)));

    public List<string> Warnings { get; } = [];
    public List<string> Partial { get; } = [];

    public static RunContext Create(string command, CoverRequest request)
    {
        var root = Path.GetFullPath(request.Root);
        if (!Directory.Exists(root)) throw new CoverInputException(InputErrorCodes.Usage, $"Root not found: {request.Root}");
        try
        {
            var path = request.ConfigPath is not null ? Path.GetFullPath(request.ConfigPath, root) : Path.Combine(root, CoverConfig.FileName);
            var exists = request.ConfigPath is not null || File.Exists(path);
            return new RunContext
            {
                Command = command, Request = request, Root = root,
                Config = exists ? CoverConfig.Load(path) : new CoverConfig(), ConfigFile = exists ? TestProjects.Relative(root, path) : null,
            };
        }
        catch (Exception e) when (e is InvalidDataException or FileNotFoundException or DirectoryNotFoundException)
        {
            throw new CoverInputException(InputErrorCodes.InvalidConfig, e.Message);
        }
    }

    /// <summary>Records why the run does not judge the whole code base.</summary>
    public void NotePartialScope(int selectedProjects, int allProjects)
    {
        if (Request.TestTarget is not null && selectedProjects < allProjects)
            Partial.Add($"--test {Request.TestTarget}: {selectedProjects} of {allProjects} test projects");
        if (HasTestFilter) Partial.Add("test filter in TEST_ARGS");
        if (Request.Paths.Count > 0) Partial.Add("paths: " + string.Join(", ", Request.Paths));
        if (Request.HasFileList) Partial.Add("--files-from");
        if (Request.Type is not null) Partial.Add("--type " + Request.Type);
        if (Unreported == UnreportedMode.Report) Partial.Add("--unreported report");
    }
}
