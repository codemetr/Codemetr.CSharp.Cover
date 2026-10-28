using System.Reflection;
using Codemetr.CSharp;
using Codemetr.CSharp.Cover;

namespace Codemetr.CSharp.Cover.Tool;

/// <summary>Everything a report is built from.</summary>
internal sealed record RunState(RunContext Context, Evidence Evidence, CoverageModel Model, IReadOnlyList<ParseError> ParseErrors, GuardInput Input,
    GuardResult Result, DiffProvenance? DiffInfo, GitState Git, IReadOnlyList<TestProject> TestProjects, int Skipped, string? BaselineWritten)
{
    public string Command => Context.Command;
    public CoverRequest Request => Context.Request;
    public IReadOnlyList<string> Partial => Context.Partial;
    public IReadOnlyList<string> Warnings => Context.Warnings;
}

/// <summary>The operations behind analyze, check and baseline.</summary>
internal static class CoverService
{
    public static string Version { get; } = typeof(CoverService).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "0.0.0";

    public static RunState Run(string command, CoverRequest request, CancellationToken cancellationToken = default)
    {
        var context = RunContext.Create(command, request);
        var config = context.Config;
        var all = TestProjects.Discover(context.Root, null, context.Configuration, config.Exclude, cancellationToken);
        var selected = context.Target is null ? all : TestProjects.Discover(context.Root, context.Target, context.Configuration, config.Exclude, cancellationToken);
        context.NotePartialScope(selected.Count, all.Count);
        var evidence = EvidenceCollector.Collect(context, all, selected, cancellationToken);

        var selection = SourceSelection.Collect(context, all);
        var (sources, parseErrors) = SourceModel.Build(selection.Documents, cancellationToken);
        EvidenceCollector.CheckFreshness(context, evidence, sources);
        var model = Model(context, evidence, sources, parseErrors);

        var git = GitState.Read(context.Root);
        var diff = DiffCollector.Collect(context, git, model, selection.IsTestSource, selection.Skipped);
        var baselinePath = request.BaselinePath is null ? Path.Combine(context.Root, CoverBaseline.FileName) : Path.GetFullPath(request.BaselinePath, context.Root);
        var input = BaselineCommand.Input(context, model, evidence, diff, baselinePath);
        var result = CoverGuard.Evaluate(input);
        string? written = null;
        if (command == CliOptions.RecordBaseline)
        {
            (input, written) = BaselineCommand.Record(context, input, result, parseErrors, baselinePath, git.Head);
            result = CoverGuard.Evaluate(input);
        }
        return new RunState(context, evidence, model, parseErrors, input, result, diff?.Provenance, git, all, selection.Skipped.Count, written);
    }

    static CoverageModel Model(RunContext context, Evidence evidence, IReadOnlyList<SourceFile> sources, IReadOnlyList<ParseError> parseErrors)
    {
        var merged = new CoverageData(new PathMapper(context.Root).Comparer);
        foreach (var (_, data) in evidence.Reports) merged.Merge(data);
        var model = CoverageModel.Build(sources, merged, context.Unreported);
        if (context.Request.Type is { } type)
        {
            model = model.Restrict(name => TypeName.Matches(name, type));
            if (model.Types.Count == 0 && parseErrors.Count == 0) throw new CoverInputException(InputErrorCodes.Usage, $"No measured type matches '{type}'");
        }
        context.Warnings.AddRange(ModelWarnings.For(model, evidence, context.Unreported));
        return model;
    }
}

/// <summary>The documents a run measures: the selected C# files, minus generated, excluded and test code.</summary>
internal sealed record SourceSelection(IReadOnlyList<SourceDocument> Documents, IReadOnlySet<string> Skipped, Func<string, bool> IsTestSource)
{
    public static SourceSelection Collect(RunContext context, IReadOnlyList<TestProject> testProjects)
    {
        var request = context.Request;
        var skipped = new SortedSet<string>(StringComparer.Ordinal);
        var paths = request.Paths.Count > 0 ? request.Paths : request.HasFileList ? (IReadOnlyList<string>)[] : ["."];
        var documents = new List<SourceDocument>();
        if (paths.Count > 0) documents.AddRange(SourceCollector.Collect(context.Root, paths, context.Config.Exclude, skipped: skipped));
        if (request.ListedFiles.Count > 0)
            documents.AddRange(SourceCollector.Collect(context.Root, request.ListedFiles.Select(p => p.Replace('\\', '/')), context.Config.Exclude, lenient: true, skipped: skipped));
        var testDirectories = testProjects.Select(p => p.Directory).Where(d => d.Length > 0).Select(d => d.TrimEnd('/') + "/").ToArray();
        bool IsTestSource(string path) => testDirectories.Any(d => path.StartsWith(d, StringComparison.Ordinal));
        var measured = documents.Where(d => !IsTestSource(d.Path)).DistinctBy(d => d.Path, StringComparer.Ordinal);
        if (request.Type is { } type) measured = measured.Where(d => d.Text.Contains(TypeName.Simple(type), StringComparison.Ordinal));
        return new SourceSelection(measured.ToArray(), skipped, IsTestSource);
    }
}
