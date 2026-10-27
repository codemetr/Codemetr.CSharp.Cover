using System.Globalization;
using System.Text.Json.Serialization;

namespace Codemetr.CSharp.Cover;

/// <summary>Line and branch coverage in percent; branch null when there are no branches.</summary>
public sealed record CoveragePercent(double? Line, double? Branch)
{
    /// <summary>From a count.</summary>
    public static CoveragePercent Of(CoverageCount count) => new(count.LinePercent, count.BranchPercent);
}

/// <summary>A branching line where some outcomes never ran.</summary>
/// <param name="Line">1-based line.</param>
/// <param name="Code">Condition source, when known.</param>
/// <param name="Covered">Outcomes that ran.</param>
/// <param name="Total">Outcomes on the line.</param>
public sealed record BranchInfo(int Line, string? Code, int Covered, int Total);

/// <summary>One finding of a run, flat for JSON consumers.</summary>
public sealed record Finding
{
    /// <summary>Finding is new.</summary>
    public const string New = "new";
    /// <summary>Recorded debt that got worse.</summary>
    public const string Worsened = "worsened";
    /// <summary>Recorded debt, not worse.</summary>
    public const string Baselined = "baselined";

    /// <summary>Stable rule id (<see cref="CoverRuleIds"/>).</summary>
    public required string Rule { get; init; }
    /// <summary>new, worsened or baselined; only the first two fail.</summary>
    public required string Status { get; init; }
    /// <summary>Full type name, if the finding concerns code.</summary>
    public string? Type { get; init; }
    /// <summary>Member with parameter types.</summary>
    public string? Member { get; init; }
    /// <summary>Root-relative file.</summary>
    public string? File { get; init; }
    /// <summary>1-based line.</summary>
    public int? Line { get; init; }
    /// <summary>Test project, for test findings.</summary>
    public string? Project { get; init; }
    /// <summary>Measured value.</summary>
    public double? Value { get; init; }
    /// <summary>Threshold or floor.</summary>
    public double? Limit { get; init; }
    /// <summary>Value recorded in the baseline.</summary>
    public double? BaselineValue { get; init; }
    /// <summary>Coverage of the code concerned.</summary>
    public CoveragePercent? Coverage { get; init; }
    /// <summary>Cyclomatic complexity of the member.</summary>
    public int? Complexity { get; init; }
    /// <summary>CRAP score of the member.</summary>
    public double? Crap { get; init; }
    /// <summary>Uncovered lines as [start, end] ranges.</summary>
    public IReadOnlyList<int[]>? UncoveredLines { get; init; }
    /// <summary>Further uncovered ranges not listed.</summary>
    public int? MoreLines { get; init; }
    /// <summary>Branching lines where some outcomes never ran.</summary>
    public IReadOnlyList<BranchInfo>? Branches { get; init; }
    /// <summary>Further branch lines not listed.</summary>
    public int? MoreBranches { get; init; }
    /// <summary>Test file that most likely belongs to this code (a hint, not a guarantee).</summary>
    public string? TestIn { get; init; }
    /// <summary>What is wrong.</summary>
    public required string Message { get; init; }
    /// <summary>The next step.</summary>
    public required string Hint { get; init; }

    /// <summary>True for new and worsened findings.</summary>
    [JsonIgnore] public bool Fails => Status is New or Worsened;

    /// <summary>Identity for matching recorded debt.</summary>
    [JsonIgnore] public string Key => Rule + "|" + Type + "|" + Member;

    internal const int MaxRanges = 8, MaxBranches = 4;

    /// <summary>Attaches line, branch and risk details of a member, capped so a finding stays small.</summary>
    public Finding WithDetails(MemberCoverage member)
    {
        var ranges = Ranges(member.UncoveredLines);
        return this with
        {
            Coverage = CoveragePercent.Of(member.Count),
            Complexity = member.Member.Complexity,
            Crap = member.Crap,
            UncoveredLines = ranges.Count == 0 ? null : ranges.Take(MaxRanges).ToArray(),
            MoreLines = ranges.Count > MaxRanges ? ranges.Count - MaxRanges : null,
            Branches = member.BranchGaps.Count == 0 ? null : member.BranchGaps.Take(MaxBranches).Select(b => new BranchInfo(b.Line, b.Code, b.Covered, b.Total)).ToArray(),
            MoreBranches = member.BranchGaps.Count > MaxBranches ? member.BranchGaps.Count - MaxBranches : null,
        };
    }

    /// <summary>Sorted lines as [start, end] ranges.</summary>
    public static IReadOnlyList<int[]> Ranges(IEnumerable<int> lines)
    {
        var result = new List<int[]>();
        foreach (var line in lines.Order())
        {
            if (result.Count > 0 && result[^1][1] + 1 >= line) result[^1][1] = Math.Max(result[^1][1], line);
            else result.Add([line, line]);
        }
        return result;
    }

    /// <summary>"52-55,60" form of ranges.</summary>
    public static string Format(IEnumerable<int[]> ranges) =>
        string.Join(",", ranges.Select(r => r[0] == r[1] ? r[0].ToString(CultureInfo.InvariantCulture) : $"{r[0]}-{r[1]}"));

    /// <summary>Invariant number with at most one decimal.</summary>
    public static string Number(double value) => value.ToString("0.#", CultureInfo.InvariantCulture);
}
