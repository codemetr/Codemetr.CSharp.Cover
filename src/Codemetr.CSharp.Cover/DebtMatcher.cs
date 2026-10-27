namespace Codemetr.CSharp.Cover;

/// <summary>Matches current violations of baselinable rules against recorded debt.</summary>
internal static class DebtMatcher
{
    const double Epsilon = GuardText.Epsilon;

    // Same multiset matching as codemetr-csharp: worst current against worst recorded per key.
    public static IEnumerable<Finding> Match(List<Finding> current, CoverBaseline? baseline, HashSet<string> measured, GuardInput input,
        CoverageModel model, List<StaleEntry> stale)
    {
        var recorded = (baseline?.Debt ?? []).Where(d => measured.Contains(d.File) || input.Complete).ToLookup(d => d.Key, StringComparer.Ordinal);
        var result = new List<Finding>();
        foreach (var group in current.GroupBy(f => f.Key, StringComparer.Ordinal))
        {
            var higherWorse = CoverRuleIds.HigherIsWorse(group.First().Rule);
            var now = (higherWorse ? group.OrderByDescending(f => f.Value) : group.OrderBy(f => f.Value)).ToArray();
            var before = (higherWorse ? recorded[group.Key].OrderByDescending(d => d.Value) : recorded[group.Key].OrderBy(d => d.Value)).ToArray();
            for (var index = 0; index < now.Length; index++)
            {
                if (index >= before.Length) { result.Add(now[index]); continue; }
                var value = now[index].Value!.Value;
                var worse = higherWorse ? value > before[index].Value + Epsilon : value < before[index].Value - Epsilon;
                var better = higherWorse ? value < before[index].Value - Epsilon : value > before[index].Value + Epsilon;
                result.Add(now[index] with { Status = worse ? Finding.Worsened : Finding.Baselined, BaselineValue = before[index].Value });
                if (better) stale.Add(Stale(before[index], value, "Recorded violation improved; run 'codemetr-csharp-cover baseline' to tighten it."));
            }
            foreach (var gone in before.Skip(now.Length))
                stale.Add(Stale(gone, null, "Recorded violation is fixed; run 'codemetr-csharp-cover baseline' to remove it."));
        }
        var violated = current.Select(f => f.Key).ToHashSet(StringComparer.Ordinal);
        foreach (var group in recorded.Where(g => !violated.Contains(g.Key)))
            foreach (var entry in group)
            {
                // A recorded member that is not measured in this run is not proven fixed.
                var exists = model.Members.Any(m => m.Member.Type == entry.Type && (entry.Member is null || m.Member.Name == entry.Member))
                    || (entry.Member is null && model.Types.Any(t => t.Type.FullName == entry.Type));
                // In a complete run a member that is gone everywhere was removed; code hidden in unmeasured files is not.
                if (exists || (input.Complete && !input.DeclaredInUnmeasuredCode(entry.Type))) stale.Add(Stale(entry, null, "Recorded violation is fixed; run 'codemetr-csharp-cover baseline' to remove it."));
            }
        return result;
    }

    static StaleEntry Stale(DebtEntry entry, double? value, string message) => new(entry.Rule, entry.Type, entry.Member, entry.File, entry.Value, value, message);
}
