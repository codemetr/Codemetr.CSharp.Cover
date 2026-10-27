using Codemetr.CSharp;
using Codemetr.CSharp.Cover;

namespace Codemetr.CSharp.Cover.Tool;

/// <summary>Builds the gate input of a run and records or tightens the baseline.</summary>
internal static class BaselineCommand
{
    public static GuardInput Input(RunContext context, CoverageModel model, Evidence evidence, DiffResult? diff, string baselinePath) => new()
    {
        Model = model, Config = context.Config, Complete = context.Partial.Count == 0, Diff = diff?.Coverage, Tests = evidence.Tests,
        Baseline = File.Exists(baselinePath) ? CoverBaseline.Load(baselinePath) : null,
        Exclusions = model.Excluded.Select(e => new ExclusionEntry(ExclusionEntry.Attribute, e.Type, e.Member, e.Reason))
            .Concat(context.Config.Exclude.Select(glob => new ExclusionEntry(ExclusionEntry.Glob, null, null, glob))).DistinctBy(e => e.Key).ToArray(),
        UnmeasuredChangedFiles = diff?.UnmeasuredChanges ?? [],
        DeclaredInUnmeasuredCode = new UnmeasuredDeclarations(context.Root, context.Config.Exclude).Contains,
    };

    public static (GuardInput Input, string Written) Record(RunContext context, GuardInput input, GuardResult result, IReadOnlyList<ParseError> parseErrors,
        string path, string? head)
    {
        if (context.Partial.Count > 0)
            throw new CoverInputException(InputErrorCodes.PartialBaseline, "Refusing to record a baseline from a partial run: " + string.Join("; ", context.Partial),
                "Record the baseline from a complete run without paths, --type, --files-from, test filters or --unreported report.");
        if (parseErrors.Count > 0) throw new CoverInputException(InputErrorCodes.ParseError, "Refusing to record a baseline while documents fail to parse.");
        if (input.Tests.Any(t => t.Counts.Failed > 0))
            throw new CoverInputException(InputErrorCodes.TestRunCrashed, "Refusing to record a baseline while tests fail; coverage of a failing run is never recorded.");
        var recorded = CoverGuard.Initial(input, result, head);
        if (input.Baseline is { } existing)
        {
            (recorded, var refused) = CoverGuard.Tighten(existing, input, result, head);
            if (refused > 0)
                context.Warnings.Add($"An existing baseline only tightens: {refused} new or worse finding(s) were not recorded. "
                    + "Fix them and run again; accepting new debt means re-creating the baseline.");
        }
        recorded.Save(path);
        return (input with { Baseline = recorded }, TestProjects.Relative(context.Root, path));
    }
}
