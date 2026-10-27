using System.Text.Json;
using Microsoft.Extensions.FileSystemGlobbing;

namespace Codemetr.CSharp.Cover;

/// <summary>Thresholds in percent (minimums) or CRAP score (maximum); null keeps the inherited value.</summary>
public sealed record CoverRules
{
    /// <summary>Minimum line coverage of the whole run.</summary>
    public double? LineCoverage { get; init; }
    /// <summary>Minimum branch coverage of the whole run.</summary>
    public double? BranchCoverage { get; init; }
    /// <summary>Minimum line coverage per type declaration.</summary>
    public double? TypeLineCoverage { get; init; }
    /// <summary>Minimum line coverage per member.</summary>
    public double? MemberLineCoverage { get; init; }
    /// <summary>Minimum line coverage of changed lines.</summary>
    public double? DiffLineCoverage { get; init; }
    /// <summary>Minimum branch coverage of changed lines.</summary>
    public double? DiffBranchCoverage { get; init; }
    /// <summary>Maximum CRAP score per member.</summary>
    public double? CrapScore { get; init; }

    /// <summary>Built-in defaults: totals 0 (the ratchet and diff coverage carry the gate), diff 80/60, CRAP 30.</summary>
    public static CoverRules Default { get; } = new()
    {
        LineCoverage = 0, BranchCoverage = 0, TypeLineCoverage = 0, MemberLineCoverage = 0,
        DiffLineCoverage = 80, DiffBranchCoverage = 60, CrapScore = 30,
    };

    /// <summary>Values of <paramref name="other"/> where set, else these.</summary>
    public CoverRules Merge(CoverRules? other) => other is null ? this : new()
    {
        LineCoverage = other.LineCoverage ?? LineCoverage,
        BranchCoverage = other.BranchCoverage ?? BranchCoverage,
        TypeLineCoverage = other.TypeLineCoverage ?? TypeLineCoverage,
        MemberLineCoverage = other.MemberLineCoverage ?? MemberLineCoverage,
        DiffLineCoverage = other.DiffLineCoverage ?? DiffLineCoverage,
        DiffBranchCoverage = other.DiffBranchCoverage ?? DiffBranchCoverage,
        CrapScore = other.CrapScore ?? CrapScore,
    };

    /// <summary>Threshold per rule id.</summary>
    public IReadOnlyDictionary<string, double> ByRule() => new Dictionary<string, double>(StringComparer.Ordinal)
    {
        [CoverRuleIds.LineCoverage] = LineCoverage ?? 0,
        [CoverRuleIds.BranchCoverage] = BranchCoverage ?? 0,
        [CoverRuleIds.TypeLineCoverage] = TypeLineCoverage ?? 0,
        [CoverRuleIds.MemberLineCoverage] = MemberLineCoverage ?? 0,
        [CoverRuleIds.DiffLineCoverage] = DiffLineCoverage ?? 0,
        [CoverRuleIds.DiffBranchCoverage] = DiffBranchCoverage ?? 0,
        [CoverRuleIds.CrapScore] = CrapScore ?? double.MaxValue,
    };

    internal void Validate(string where)
    {
        foreach (var value in new[] { LineCoverage, BranchCoverage, TypeLineCoverage, MemberLineCoverage, DiffLineCoverage, DiffBranchCoverage })
            if (value is < 0 or > 100) throw new InvalidDataException($"{where}: coverage thresholds must be between 0 and 100");
        if (CrapScore is < 1) throw new InvalidDataException($"{where}: crapScore must be at least 1");
    }
}

/// <summary>Rules for files (glob) and/or one type (full name); later overrides win.</summary>
public sealed record CoverOverride
{
    /// <summary>Glob over root-relative paths.</summary>
    public string? Files { get; init; }
    /// <summary>Full type name.</summary>
    public string? Type { get; init; }
    /// <summary>Rules applied on match.</summary>
    public CoverRules? Rules { get; init; }
}

/// <summary>Diff settings.</summary>
public sealed record DiffSettings
{
    /// <summary>Default base reference for diff coverage, for example <c>origin/main</c>.</summary>
    public string? Base { get; init; }
}

/// <summary>How the tool runs the tests.</summary>
public sealed record TestSettings
{
    /// <summary>Solution, project or directory to test; default: every test project below the root.</summary>
    public string? Target { get; init; }
    /// <summary>Build configuration (default Debug).</summary>
    public string? Configuration { get; init; }
    /// <summary>Arguments passed to every test run, like TEST_ARGS on the command line.</summary>
    public string[] Args { get; init; } = [];
}

/// <summary>Default thresholds of <c>analyze --below</c>, separate from the gate's rules.</summary>
public sealed record WorklistSettings
{
    /// <summary>Line coverage threshold.</summary>
    public double? Line { get; init; }
    /// <summary>Branch coverage threshold.</summary>
    public double? Branch { get; init; }
    /// <summary>member, type or file.</summary>
    public string? Level { get; init; }
}

/// <summary>Contents of <c>codemetr-cover.json</c>.</summary>
public sealed record CoverConfig
{
    /// <summary>Conventional file name in the root.</summary>
    public const string FileName = "codemetr-cover.json";

    /// <summary>Gate thresholds on top of <see cref="CoverRules.Default"/>.</summary>
    public CoverRules? Rules { get; init; }
    /// <summary>Overrides in order.</summary>
    public CoverOverride[] Overrides { get; init; } = [];
    /// <summary>Globs over root-relative paths that are not measured (listed as exclusions).</summary>
    public string[] Exclude { get; init; } = [];
    /// <summary>zero, report or ignore.</summary>
    public string Unreported { get; init; } = UnreportedMode.Zero;
    /// <summary>Diff settings.</summary>
    public DiffSettings? Diff { get; init; }
    /// <summary>Test run settings.</summary>
    public TestSettings? Test { get; init; }
    /// <summary>Worklist defaults.</summary>
    public WorklistSettings? Worklist { get; init; }

    /// <summary>Effective gate thresholds of the run.</summary>
    public CoverRules Effective => CoverRules.Default.Merge(Rules);

    /// <summary>Loads and validates a configuration file.</summary>
    public static CoverConfig Load(string path)
    {
        CoverConfig? config;
        try { config = JsonSerializer.Deserialize<CoverConfig>(File.ReadAllBytes(path), Codemetr.CSharp.CodemetrJson.Options); }
        catch (JsonException e) { throw new InvalidDataException($"Invalid {FileName}: {e.Message}"); }
        if (config is null) throw new InvalidDataException($"Empty {FileName}");
        if (config.Overrides is null || config.Exclude is null || config.Exclude.Any(string.IsNullOrWhiteSpace))
            throw new InvalidDataException($"{FileName}: 'overrides' and 'exclude' must be arrays of non-empty values");
        if (!UnreportedMode.All.Contains(config.Unreported)) throw new InvalidDataException($"{FileName}: 'unreported' must be zero, report or ignore");
        if (config.Unreported == UnreportedMode.Ignore) throw new InvalidDataException($"{FileName}: 'unreported' cannot be ignore in the configuration; use --unreported ignore with analyze");
        config.Rules?.Validate(FileName);
        foreach (var rule in config.Overrides)
        {
            if (rule?.Rules is null) throw new InvalidDataException($"{FileName}: an override needs 'rules'");
            if (rule.Files is null && rule.Type is null) throw new InvalidDataException($"{FileName}: an override needs 'files' or 'type'");
            rule.Rules.Validate(FileName);
        }
        if (config.Worklist?.Level is { } level && level is not ("member" or "type" or "file"))
            throw new InvalidDataException($"{FileName}: worklist.level must be member, type or file");
        return config;
    }

    /// <summary>Effective thresholds for code of <paramref name="type"/> in <paramref name="file"/>.</summary>
    public CoverRules RulesFor(string file, string? type)
    {
        var rules = Effective;
        foreach (var rule in Overrides)
        {
            var fileMatches = rule.Files is null || Glob(rule.Files).Match(file).HasMatches;
            var typeMatches = rule.Type is null || (type is not null && (type == rule.Type || StripGenerics(type) == rule.Type));
            if (fileMatches && typeMatches) rules = rules.Merge(rule.Rules);
        }
        return rules;
    }

    static Matcher Glob(string pattern)
    {
        var matcher = new Matcher(StringComparison.Ordinal);
        matcher.AddInclude(pattern);
        return matcher;
    }

    internal static string StripGenerics(string name)
    {
        var result = new System.Text.StringBuilder(name.Length);
        var depth = 0;
        foreach (var character in name)
        {
            if (character == '<') depth++;
            else if (character == '>') depth--;
            else if (depth == 0) result.Append(character);
        }
        return result.ToString();
    }
}
