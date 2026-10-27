namespace Codemetr.CSharp.Cover;

/// <summary>Coverage of one source line: execution count and branch outcomes on that line.</summary>
/// <param name="Hits">How often the line ran; 0 means uncovered.</param>
/// <param name="CoveredBranches">Branch outcomes on this line that ran.</param>
/// <param name="Branches">Branch outcomes on this line; 0 when the line has no branch.</param>
public sealed record LineHit(int Hits, int CoveredBranches, int Branches)
{
    /// <summary>Combines two measurements of the same line from different test runs.</summary>
    /// <remarks>
    /// Hits add up. Cobertura only gives branch counts per line, not which outcome ran, so the merge keeps the
    /// highest covered count: a lower bound that never claims more than one run proved.
    /// </remarks>
    public LineHit Merge(LineHit other) => new(
        (int)Math.Min(int.MaxValue, (long)Hits + other.Hits),
        Math.Max(CoveredBranches, other.CoveredBranches),
        Math.Max(Branches, other.Branches));
}
