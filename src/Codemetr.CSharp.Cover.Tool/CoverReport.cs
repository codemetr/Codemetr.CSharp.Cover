using System.Text.Json.Serialization;
using Codemetr.CSharp.Cover;

namespace Codemetr.CSharp.Cover.Tool;

/// <summary>JSON document returned by analyze, check and baseline.</summary>
internal sealed record CoverReport
{
    public int Schema { get; init; } = ReportBuilder.ReportSchema;
    public string Tool { get; init; } = "codemetr-csharp-cover";
    public required string Version { get; init; }
    public required string Command { get; init; }
    public required bool Passed { get; init; }
    public bool Complete { get; init; } = true;
    public Provenance? Provenance { get; init; }
    public ReportSummary? Summary { get; init; }
    public IReadOnlyList<Finding> Violations { get; init; } = [];
    public DiffSection? Diff { get; init; }
    public IReadOnlyList<SkippedCode> Unreported { get; init; } = [];
    public IReadOnlyList<ExclusionItem> Exclusions { get; init; } = [];
    public IReadOnlyList<StaleEntry> Stale { get; init; } = [];
    public IReadOnlyList<Codemetr.CSharp.ParseError> ParseErrors { get; init; } = [];
    public IReadOnlyList<InputError> InputErrors { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
    public IReadOnlyList<string> Hotspots { get; init; } = [];
    public WorklistSection? Worklist { get; init; }
    public IReadOnlyList<TypeReport>? Types { get; init; }
    public string? BaselineWritten { get; init; }
}

internal sealed record InputError(string Code, string Message, string? Hint);

internal sealed record Provenance(string Source, string? Head, bool? Dirty, string? Config, string Unreported,
    IReadOnlyList<ProjectReport> TestProjects, IReadOnlyList<string> Reports, DiffProvenance? Diff, IReadOnlyList<string> Partial);

internal sealed record ProjectReport(string Project, string? Platform, string? Engine, IReadOnlyList<string> Reports, TestCounts? Tests);

internal sealed record DiffProvenance(string Base, string? MergeBase, string? Head, bool WorkingTree, int Files, int ChangedLines);

internal sealed record ReportSummary
{
    public int Files { get; init; }
    public int Types { get; init; }
    public int Members { get; init; }
    public double? Line { get; init; }
    public double? Branch { get; init; }
    public int CoveredLines { get; init; }
    public int CoverableLines { get; init; }
    public int CoveredBranches { get; init; }
    public int Branches { get; init; }
    public double? DiffLine { get; init; }
    public double? DiffBranch { get; init; }
    public int Violations { get; init; }
    public int New { get; init; }
    public int Worsened { get; init; }
    public int Baselined { get; init; }
    public int Stale { get; init; }
    public int Tightenable { get; init; }
    public int Unreported { get; init; }
    public int Excluded { get; init; }
    public int Skipped { get; init; }
    public int Unmapped { get; init; }
    public int Listed { get; init; }
    public int Omitted { get; init; }
    public BelowCounts? Below { get; init; }
}

internal sealed record BelowCounts(int Members, int Types, int Files);

internal sealed record DiffSection(IReadOnlyList<DiffGap> Uncovered, IReadOnlyList<string> UnmeasuredChange, IReadOnlyList<RemovedMember> RemovedMembers);

internal sealed record DiffGap(string File, string? Type, string? Member, IReadOnlyList<int[]> Lines);

internal sealed record RemovedMember(string Type, string Member, string File);

internal sealed record ExclusionItem(string Kind, string? Type, string? Member, string? File, int? Line, string? Pattern, bool? Baselined);

internal sealed record WorklistSection
{
    /// <summary>Always null: a worklist is never a gate result.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)] public object? Verdict { get; init; }
    public required bool Complete { get; init; }
    public required WorklistThresholds Thresholds { get; init; }
    public required string Level { get; init; }
    public required string Sort { get; init; }
    public required int Total { get; init; }
    public required int Listed { get; init; }
    public required int Omitted { get; init; }
    public required IReadOnlyList<WorklistItem> Items { get; init; }
}

internal sealed record WorklistThresholds(double Line, double? Branch, string Source);

internal sealed record TypeReport(string Type, string File, int Line, CoveragePercent Coverage, int CoverableLines, int Branches, IReadOnlyList<MemberReport> Members);

internal sealed record MemberReport(string Member, int Line, CoveragePercent Coverage, int CoveredLines, int CoverableLines, int CoveredBranches, int Branches,
    int Complexity, double Crap, bool Reported, IReadOnlyList<int[]> UncoveredLines, IReadOnlyList<BranchInfo> BranchGaps);

/// <summary>Result of doctor.</summary>
internal sealed record DoctorReport(int Schema, string Tool, string Version, string Command, bool Passed, IReadOnlyList<DoctorProject> TestProjects,
    IReadOnlyList<InputError> InputErrors, IReadOnlyList<string> Warnings);

internal sealed record DoctorProject(string Project, string Platform, IReadOnlyList<string> Frameworks, string RequiredEngine, string? Engine, string? Fix);
