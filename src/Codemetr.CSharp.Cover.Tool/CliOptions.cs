namespace Codemetr.CSharp.Cover.Tool;

/// <summary>What a run measures and how.</summary>
internal sealed record CoverRequest
{
    public required string Root { get; init; }
    public IReadOnlyList<string> Paths { get; init; } = [];
    public IReadOnlyList<string> ListedFiles { get; init; } = [];
    public bool HasFileList { get; init; }
    public string? Type { get; init; }
    public string? ConfigPath { get; init; }
    public string? BaselinePath { get; init; }
    public string? TestTarget { get; init; }
    public IReadOnlyList<string> Reports { get; init; } = [];
    public IReadOnlyList<string> TestResults { get; init; } = [];
    public IReadOnlyList<(string From, string To)> PathMaps { get; init; } = [];
    public string? Configuration { get; init; }
    public bool NoBuild { get; init; }
    public IReadOnlyList<string> TestArgs { get; init; } = [];
    public string? Base { get; init; }
    public string? DiffFile { get; init; }
    public string? Unreported { get; init; }
    public bool Below { get; init; }
    public double? BelowLine { get; init; }
    public double? BelowBranch { get; init; }
    public string? Level { get; init; }
    public string Sort { get; init; } = "gap";
    public int Top { get; init; } = CliOptions.DefaultTop;
    public string? Member { get; init; }
}

/// <summary>Parsed command line.</summary>
internal sealed record CliOptions(string Command, CoverRequest Request, string Format, string? Output)
{
    public const string Json = "json", Text = "text", Github = "github";
    public const string Analyze = "analyze", Check = "check", RecordBaseline = "baseline", Doctor = "doctor";
    public const int DefaultTop = 10;

    public const string Usage = """
        codemetr-csharp-cover - C# line/branch coverage per type and member (Coverlet), diff coverage, CRAP, ratchet gate

        Usage:
          codemetr-csharp-cover analyze  [PATH...] [options] [-- TEST_ARGS]   overview, riskiest members, worklist (exit 0 unless input error)
          codemetr-csharp-cover check    [PATH...] [options] [-- TEST_ARGS]   gate: thresholds, diff coverage, ratchet, test results
          codemetr-csharp-cover baseline [PATH...] [options] [-- TEST_ARGS]   record coverage floors; later runs only raise them
          codemetr-csharp-cover doctor   [options]                            test projects, platform (VSTest/MTP), Coverlet, fix commands

        Coverage source (default: build and run the test projects; coverlet.collector or coverlet.MTP detected per project):
          --test TARGET          .sln/.slnx/.csproj/dir to test (default: config test.target, else all test projects below ROOT)
          --configuration NAME   build configuration (default: config test.configuration, else Debug)
          --no-build             run existing binaries; refused when a source is newer than the test assembly
          --reports GLOB         read existing Cobertura/OpenCover files instead of running tests (repeatable)
          --test-results GLOB    TRX results of those reports (required by check and baseline with --reports; repeatable)
          --path-map FROM=TO     rewrite source paths of reports produced elsewhere (repeatable)
          -- TEST_ARGS           passed to every test run; filters make the run partial

        Scope:
          --root DIR             repo root: base for paths, config and baseline (default: current directory)
          --config FILE          rules file (default: ROOT/codemetr-cover.json if present, else built-in defaults)
          --baseline FILE        baseline file (default: ROOT/codemetr-cover.baseline.json)
          --type NAME            only types with this simple, full or full-name suffix; partial run
          --files-from FILE      only the listed files ('-' = stdin); partial run
          --base REF             diff coverage against merge-base(REF, HEAD) plus uncommitted changes
                                 (default: config diff.base, else HEAD = uncommitted changes only)
          --diff-file FILE       unified diff instead of git ('-' = stdin)
          --unreported zero|report|ignore   code without coverage data (default: config, else zero; ignore: analyze only)

        Worklist (analyze only):
          --below [LINE[,BRANCH]]   every item under these thresholds, independent of baseline and gate (no value: config worklist)
          --level member|type|file  granularity (default: config worklist.level, else member)
          --sort gap|crap|path      order (default: gap = lines and branches missing to the threshold)

        Output:
          --format json|text|github   stdout format (default: json)
          --output FILE          also write the complete JSON report (no --top limit) to FILE
          --top N                items listed on stdout (default: 10); counts are always complete
          --member NAME          full line and branch map of matching members (simple, full or suffix name)

        Exit codes: 0 passed, 1 findings (including failed tests), 2 input error or untrustworthy coverage data.
        """;

    public static CliOptions Parse(string[] args, TextReader stdin) => CliParser.Parse(args, stdin);
}
