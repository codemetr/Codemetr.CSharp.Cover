namespace Codemetr.CSharp.Cover;

/// <summary>Stable rule identifiers used in reports and baselines.</summary>
public static class CoverRuleIds
{
    /// <summary>Minimum line coverage of the whole run.</summary>
    public const string LineCoverage = "lineCoverage";
    /// <summary>Minimum branch coverage of the whole run.</summary>
    public const string BranchCoverage = "branchCoverage";
    /// <summary>Minimum line coverage per type declaration.</summary>
    public const string TypeLineCoverage = "typeLineCoverage";
    /// <summary>Minimum line coverage per member.</summary>
    public const string MemberLineCoverage = "memberLineCoverage";
    /// <summary>Minimum line coverage of changed lines.</summary>
    public const string DiffLineCoverage = "diffLineCoverage";
    /// <summary>Minimum branch coverage of changed lines.</summary>
    public const string DiffBranchCoverage = "diffBranchCoverage";
    /// <summary>Maximum CRAP score per member.</summary>
    public const string CrapScore = "crapScore";
    /// <summary>Coverage of a recorded member or file fell below its floor.</summary>
    public const string CoverageDropped = "coverageDropped";
    /// <summary>A test failed.</summary>
    public const string TestsFailed = "testsFailed";
    /// <summary>More skipped tests than recorded.</summary>
    public const string SkippedTestsIncreased = "skippedTestsIncreased";
    /// <summary>An exclusion that the baseline does not record.</summary>
    public const string NewCoverageExclusion = "newCoverageExclusion";
    /// <summary>Changed lines in files that are never measured.</summary>
    public const string UnmeasuredChange = "unmeasuredChange";
    /// <summary>Recorded code that moved into code that is never measured.</summary>
    public const string NoLongerMeasured = "noLongerMeasured";
    /// <summary>A threshold looser than the one recorded with the baseline.</summary>
    public const string ThresholdLowered = "thresholdLowered";

    /// <summary>Rules whose violations a baseline can record as debt.</summary>
    public static readonly IReadOnlyList<string> Baselinable = [TypeLineCoverage, MemberLineCoverage, CrapScore];

    /// <summary>True when a higher value is worse.</summary>
    public static bool HigherIsWorse(string rule) => rule == CrapScore;

    /// <summary>The next concrete step for the rule.</summary>
    public static string Hint(string rule) => rule switch
    {
        LineCoverage or BranchCoverage => "Add tests for the members with the largest gap first (analyze --below); do not exclude code to raise the total.",
        TypeLineCoverage or MemberLineCoverage => "Write tests that call this code and assert its results for each branch.",
        DiffLineCoverage or DiffBranchCoverage => "Add tests that execute and assert the uncovered changed lines listed in diff.uncovered.",
        CrapScore => "Write tests that cover each branch and assert the result, or split the member to reduce its complexity; do not exclude, delete or move it to lower the score.",
        CoverageDropped => "Coverage of recorded code fell: restore or replace the tests that covered it.",
        TestsFailed => "Fix the failing tests; coverage of a failing run never counts as passing.",
        SkippedTestsIncreased => "Run the skipped tests again or remove the skip; skipping tests is not a way to pass.",
        NewCoverageExclusion => "Remove the exclusion and test the code; accepting a new exclusion is a policy decision for the baseline owner.",
        UnmeasuredChange => "This change lies in generated or excluded code that is never measured; move hand-written code to a measured file.",
        NoLongerMeasured => "Recorded code moved into a generated, excluded or build-output file; measure it again or have the owner re-create the baseline.",
        ThresholdLowered => "A threshold is looser than when the baseline was recorded; restore it in codemetr-cover.json.",
        _ => string.Empty,
    };
}
