using Codemetr.CSharp.Cover;

namespace Codemetr.CSharp.Cover.Tool;

/// <summary>Per-type and per-member detail (--member, and the complete report of --output).</summary>
internal static class TypeSection
{
    public static IReadOnlyList<TypeReport> Types(CoverageModel model, string? memberFilter)
    {
        var members = model.Members.Where(m => memberFilter is null || MemberMatches(m.Member, memberFilter)).ToLookup(m => (m.Member.Type, m.Member.File));
        return model.Types.Where(t => memberFilter is null || members[(t.Type.FullName, t.Type.File)].Any())
            .Select(t => new TypeReport(t.Type.FullName, t.Type.File, t.Type.Line, CoveragePercent.Of(t.Count), t.Count.CoverableLines, t.Count.Branches,
                members[(t.Type.FullName, t.Type.File)].Select(Member).ToArray()))
            .ToArray();
    }

    static MemberReport Member(MemberCoverage m) => new(m.Member.Name, m.Member.Line, CoveragePercent.Of(m.Count),
        m.Count.CoveredLines, m.Count.CoverableLines, m.Count.CoveredBranches, m.Count.Branches, m.Member.Complexity, m.Crap, m.Reported,
        Finding.Ranges(m.UncoveredLines), m.BranchGaps.Select(b => new BranchInfo(b.Line, b.Code, b.Covered, b.Total)).ToArray());

    // "Submit", "Submit(Order)", "OrderService.Submit" or "Shop.OrderService.Submit(Order)".
    static bool MemberMatches(SourceMember member, string query)
    {
        var full = $"{member.Type}.{member.Name}";
        var withoutSignature = full.Split('(')[0];
        return member.Name == query || member.Name.Split('(')[0] == query || full == query || full.EndsWith("." + query, StringComparison.Ordinal)
            || withoutSignature.EndsWith("." + query, StringComparison.Ordinal);
    }
}
