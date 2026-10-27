using Codemetr.CSharp.Cover;

namespace Codemetr.CSharp.Cover.Tool;

/// <summary>Diff coverage of a run, with where the diff came from.</summary>
internal sealed record DiffResult(DiffCoverage Coverage, DiffProvenance Provenance, IReadOnlyList<string> UnmeasuredChanges);

/// <summary>Takes the changed lines (git or a diff file) and measures their coverage.</summary>
internal static class DiffCollector
{
    public static DiffResult? Collect(RunContext context, GitState git, CoverageModel model, Func<string, bool> isTestSource, IReadOnlySet<string> skipped)
    {
        if (Changes(context, git) is not var (changed, diffBase, mergeBase)) return null;
        var normalized = changed.Where(c => c.Key.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) && !isTestSource(c.Key))
            .ToDictionary(c => c.Key, c => (IReadOnlySet<int>)c.Value, StringComparer.Ordinal);
        var count = model.CountLines(normalized, out var uncovered);
        return new DiffResult(new DiffCoverage(diffBase, count, uncovered),
            new DiffProvenance(diffBase, mergeBase, git.Head, context.Request.DiffFile is null, normalized.Count, normalized.Values.Sum(v => v.Count)),
            normalized.Keys.Where(skipped.Contains).Order(StringComparer.Ordinal).ToArray());
    }

    static (Dictionary<string, SortedSet<int>> Changed, string Base, string? MergeBase)? Changes(RunContext context, GitState git)
    {
        var request = context.Request;
        if (request.DiffFile is not null)
        {
            var text = request.DiffFile == "-" ? Console.In.ReadToEnd() : File.ReadAllText(Path.GetFullPath(request.DiffFile, context.Root));
            return (UnifiedDiff.Parse(text), "diff-file", null);
        }
        var explicitBase = request.Base ?? context.Config.Diff?.Base;
        if (!git.IsRepository)
        {
            if (explicitBase is not null)
                throw new CoverInputException(InputErrorCodes.UnresolvedBase, $"--base {explicitBase}: {context.Root} is not a git repository.", "Pass --diff-file instead.");
            context.Warnings.Add("Not a git repository: no diff coverage.");
            return null;
        }
        if (git.Head is null) { context.Warnings.Add("The repository has no commit yet: no diff coverage."); return null; }
        var diffBase = explicitBase ?? "HEAD";
        var mergeBase = git.Git.MergeBase(diffBase)
            ?? throw new CoverInputException(InputErrorCodes.UnresolvedBase, $"Cannot resolve the merge base of {diffBase} and HEAD.",
                "Fetch the history (actions/checkout fetch-depth: 0) or pass --diff-file.");
        return (UnifiedDiff.Parse(git.Git.Diff(mergeBase)), diffBase, mergeBase);
    }
}

/// <summary>Git facts of the root, read once per run.</summary>
internal sealed record GitState(Git Git, bool IsRepository, string? Head, bool? Dirty)
{
    public static GitState Read(string root)
    {
        var git = new Git(root);
        var isRepository = git.IsRepository;
        return new GitState(git, isRepository, isRepository ? git.Head() : null, isRepository ? git.Dirty() : null);
    }
}
