namespace Codemetr.CSharp.Cover;

/// <summary>The ratchet: member floors, the exclusion inventory and recorded thresholds.</summary>
internal static class FloorRatchet
{
    const double Epsilon = GuardText.Epsilon;

    public static int Floors(List<Finding> findings, CoverageModel model, CoverBaseline baseline, GuardInput input, List<MemberFloor> removed)
    {
        var tightenable = 0;
        var members = model.Members.GroupBy(m => m.Member.Key, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var excludedKeys = model.Excluded.Where(e => e.Member is not null).Select(e => e.Type + "::" + e.Member).ToHashSet(StringComparer.Ordinal);
        var measured = model.Files.Select(f => f.File.Path).ToHashSet(StringComparer.Ordinal);
        foreach (var floor in baseline.Members)
        {
            if (members.TryGetValue(floor.Key, out var member))
            {
                var line = member.Count.LinePercent ?? 100;
                var branch = member.Count.BranchPercent;
                var lineDropped = line < floor.Line - Epsilon;
                var branchDropped = floor.Branch is { } fb && (branch ?? 100) < fb - Epsilon;
                if (lineDropped || branchDropped)
                    findings.Add(new Finding
                    {
                        Rule = CoverRuleIds.CoverageDropped, Status = Finding.New, Type = floor.Type, Member = floor.Member, File = member.Member.File,
                        Line = member.Member.Line, Value = lineDropped ? line : branch, Limit = lineDropped ? floor.Line : floor.Branch,
                        Message = $"{floor.Type}.{floor.Member}: {(lineDropped ? $"line coverage {Pct(line)} below recorded {Pct(floor.Line)}" : $"branch coverage {Pct(branch)} below recorded {Pct(floor.Branch)}")}.",
                        Hint = CoverRuleIds.Hint(CoverRuleIds.CoverageDropped),
                    }.WithDetails(member));
                else if (line > floor.Line + Epsilon || (branch ?? 0) > (floor.Branch ?? 0) + Epsilon) tightenable++;
                continue;
            }
            if (excludedKeys.Contains(floor.Key)) continue; // reported as a new exclusion
            if (!input.Complete && !measured.Contains(floor.File)) continue;
            if (input.DeclaredInUnmeasuredCode(floor.Type))
                findings.Add(new Finding
                {
                    Rule = CoverRuleIds.NoLongerMeasured, Status = Finding.New, Type = floor.Type, Member = floor.Member, File = floor.File,
                    Message = $"{floor.Type}.{floor.Member}: recorded member is no longer measured (its type is declared in generated or excluded code).",
                    Hint = CoverRuleIds.Hint(CoverRuleIds.NoLongerMeasured),
                });
            else if (input.Complete) { removed.Add(floor); tightenable++; }
        }
        var floored = baseline.Members.Select(f => f.Key).ToHashSet(StringComparer.Ordinal);
        tightenable += members.Keys.Count(key => !floored.Contains(key));
        return tightenable;
    }

    public static int Inventory(List<Finding> findings, GuardInput input, CoverBaseline baseline)
    {
        var recorded = baseline.Exclusions.Select(e => e.Key).ToHashSet(StringComparer.Ordinal);
        foreach (var exclusion in input.Exclusions.Where(e => !recorded.Contains(e.Key)))
        {
            var what = exclusion.Kind == ExclusionEntry.Glob ? $"exclude glob '{exclusion.Pattern}'"
                : $"[{exclusion.Pattern}] on {exclusion.Type}{(exclusion.Member is null ? "" : "." + exclusion.Member)}";
            var site = exclusion.Kind == ExclusionEntry.Attribute
                ? input.Model.Excluded.FirstOrDefault(e => e.Type == exclusion.Type && e.Member == exclusion.Member) : null;
            findings.Add(new Finding
            {
                Rule = CoverRuleIds.NewCoverageExclusion, Status = Finding.New, Type = exclusion.Type, Member = exclusion.Member,
                File = site?.File, Line = site?.Line,
                Message = $"New coverage exclusion: {what}.", Hint = CoverRuleIds.Hint(CoverRuleIds.NewCoverageExclusion),
            });
        }
        var current = input.Exclusions.Select(e => e.Key).ToHashSet(StringComparer.Ordinal);
        return input.Complete ? baseline.Exclusions.Count(e => !current.Contains(e.Key)) : 0;
    }

    public static void ThresholdsLowered(List<Finding> findings, CoverRules rules, CoverBaseline baseline)
    {
        foreach (var (rule, value) in rules.ByRule())
        {
            if (!baseline.RecordedAt.Thresholds.TryGetValue(rule, out var recorded)) continue;
            var looser = CoverRuleIds.HigherIsWorse(rule) ? value > recorded + Epsilon : value < recorded - Epsilon;
            if (looser)
                findings.Add(new Finding
                {
                    Rule = CoverRuleIds.ThresholdLowered, Status = Finding.New, Value = value, Limit = recorded,
                    Message = $"Threshold {rule} is {Finding.Number(value)}, looser than {Finding.Number(recorded)} recorded with the baseline.",
                    Hint = CoverRuleIds.Hint(CoverRuleIds.ThresholdLowered),
                });
        }
    }

    static string Pct(double? value) => GuardText.Pct(value);
}
