using Codemetr.CSharp.Cover;

namespace Codemetr.CSharp.Cover.Tool;

/// <summary>The worklist section of analyze --below.</summary>
internal static class WorklistSectionBuilder
{
    static (double Line, double? Branch, string Source, string Level)? WorklistSettings(RunState state)
    {
        var request = state.Request;
        if (!request.Below) return null;
        var configured = state.Context.Config.Worklist;
        var level = request.Level ?? configured?.Level ?? "member";
        if (request.BelowLine is not null || request.BelowBranch is not null) return (request.BelowLine ?? 0, request.BelowBranch, "cli", level);
        if (configured?.Line is null && configured?.Branch is null)
            throw new CoverInputException(InputErrorCodes.Usage, "--below needs thresholds (e.g. --below 80,60) or 'worklist' in codemetr-cover.json");
        return (configured.Line ?? 0, configured.Branch, "config", level);
    }

    public static BelowCounts? BelowCounts(RunState state) => WorklistSettings(state) is { } s
        ? new BelowCounts(Count(state, s, "member"), Count(state, s, "type"), Count(state, s, "file"))
        : null;

    static int Count(RunState state, (double Line, double? Branch, string Source, string Level) s, string level) =>
        Codemetr.CSharp.Cover.Worklist.Build(state.Model, s.Line, s.Branch, level, "path").Count;

    public static WorklistSection? Worklist(RunState state, TestLocator locator, int top)
    {
        if (WorklistSettings(state) is not { } settings) return null;
        var gate = state.Result.Findings.Where(f => f.Type is not null && f.Member is not null)
            .GroupBy(f => f.Type + "::" + f.Member).ToDictionary(g => g.Key,
                g => g.Any(f => f.Status == Finding.New) ? Finding.New : g.Any(f => f.Status == Finding.Worsened) ? Finding.Worsened : Finding.Baselined,
                StringComparer.Ordinal);
        var items = Codemetr.CSharp.Cover.Worklist.Build(state.Model, settings.Line, settings.Branch, settings.Level, state.Request.Sort, gate);
        var listed = items.Take(top).Select(i => i.Type is not null && i.Level != "file" ? i with { TestIn = locator.For(i.Type) } : i).ToArray();
        var rules = state.Context.Config.Effective;
        var looser = $"Worklist threshold {Finding.Number(settings.Line)}% is below the gate's memberLineCoverage {Finding.Number(rules.MemberLineCoverage ?? 0)}%.";
        if (settings.Level == "member" && settings.Line < (rules.MemberLineCoverage ?? 0) && !state.Context.Warnings.Contains(looser)) state.Context.Warnings.Add(looser);
        return new WorklistSection
        {
            Complete = state.Partial.Count == 0, Thresholds = new WorklistThresholds(settings.Line, settings.Branch, settings.Source),
            Level = settings.Level, Sort = state.Request.Sort, Total = items.Count, Listed = listed.Length, Omitted = items.Count - listed.Length, Items = listed,
        };
    }
}
