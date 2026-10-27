using System.Globalization;
using Codemetr.CSharp.Cover;

namespace Codemetr.CSharp.Cover.Tool;

/// <summary>Turns arguments into <see cref="CliOptions"/>: one small handler per option, validation at the end.</summary>
internal sealed class CliParser
{
    CoverRequest request = new() { Root = Environment.CurrentDirectory };
    string format = CliOptions.Json;
    string? output;
    readonly Collected collected = new();
    readonly TextReader stdin;

    sealed class Collected
    {
        public readonly List<string> Paths = [], Listed = [], Reports = [], Results = [], TestArgs = [], WorklistOptions = [];
        public readonly List<(string, string)> Maps = [];
    }

    CliParser(TextReader stdin) => this.stdin = stdin;

    static readonly Dictionary<string, Action<CliParser, string>> WithValue = new(StringComparer.Ordinal)
    {
        ["--root"] = (p, v) => p.request = p.request with { Root = v },
        ["--type"] = (p, v) => p.request = p.request with { Type = v },
        ["--config"] = (p, v) => p.request = p.request with { ConfigPath = v },
        ["--baseline"] = (p, v) => p.request = p.request with { BaselinePath = v },
        ["--test"] = (p, v) => p.request = p.request with { TestTarget = v },
        ["--configuration"] = (p, v) => p.request = p.request with { Configuration = v },
        ["--reports"] = (p, v) => p.collected.Reports.Add(v),
        ["--test-results"] = (p, v) => p.collected.Results.Add(v),
        ["--path-map"] = (p, v) => p.collected.Maps.Add(PathMap(v)),
        ["--base"] = (p, v) => p.request = p.request with { Base = v },
        ["--diff-file"] = (p, v) => p.request = p.request with { DiffFile = v },
        ["--unreported"] = (p, v) => p.request = p.request with { Unreported = OneOf(v, UnreportedMode.All, "--unreported must be zero, report or ignore") },
        ["--level"] = (p, v) => p.Worklist("--level", p.request with { Level = OneOf(v, Codemetr.CSharp.Cover.Worklist.Levels, "--level must be member, type or file") }),
        ["--sort"] = (p, v) => p.Worklist("--sort", p.request with { Sort = OneOf(v, Codemetr.CSharp.Cover.Worklist.Sorts, "--sort must be gap, crap or path") }),
        ["--format"] = (p, v) => p.format = OneOf(v, [CliOptions.Json, CliOptions.Text, CliOptions.Github], "--format must be json, text or github"),
        ["--output"] = (p, v) => p.output = v,
        ["--top"] = (p, v) => p.request = p.request with { Top = NonNegative(v) },
        ["--member"] = (p, v) => p.request = p.request with { Member = v },
        ["--files-from"] = (p, v) => p.ReadFileList(v),
    };

    public static CliOptions Parse(string[] args, TextReader stdin)
    {
        if (args.Length == 0) throw new ArgumentException("Missing command");
        var command = args[0];
        if (command is not (CliOptions.Analyze or CliOptions.Check or CliOptions.RecordBaseline or CliOptions.Doctor))
            throw new ArgumentException($"Unknown command '{command}'");
        var parser = new CliParser(stdin);
        for (var index = 1; index < args.Length; index++) index = parser.Accept(args, index);
        return parser.Build(command);
    }

    // Returns the index of the last argument consumed.
    int Accept(string[] args, int index)
    {
        var arg = args[index];
        if (arg == "--") { collected.TestArgs.AddRange(args[(index + 1)..]); return args.Length; }
        if (WithValue.TryGetValue(arg, out var apply))
        {
            if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1])) throw new ArgumentException($"Missing value for {arg}");
            apply(this, args[index + 1]);
            return index + 1;
        }
        switch (arg)
        {
            case "--no-build": request = request with { NoBuild = true }; return index;
            case "--below": return Below(args, index);
            default:
                if (arg.StartsWith("--", StringComparison.Ordinal)) throw new ArgumentException($"Unknown option '{arg}'");
                collected.Paths.Add(arg);
                return index;
        }
    }

    void Worklist(string option, CoverRequest updated)
    {
        collected.WorklistOptions.Add(option);
        request = updated;
    }

    // --below, --below 80, --below 80,60, --below ,60
    int Below(string[] args, int index)
    {
        Worklist("--below", request with { Below = true });
        if (index + 1 >= args.Length || !TryThresholds(args[index + 1], out var line, out var branch)) return index;
        request = request with { BelowLine = line, BelowBranch = branch };
        return index + 1;
    }

    void ReadFileList(string source)
    {
        request = request with { HasFileList = true };
        var lines = source == "-" ? ReadLines(stdin) : File.ReadAllLines(source);
        collected.Listed.AddRange(lines.Select(line => line.Trim()).Where(line => line.Length > 0));
    }

    CliOptions Build(string command)
    {
        // A worklist never decides a verdict, so its options exist only where there is no verdict.
        if (collected.WorklistOptions.Count > 0 && command != CliOptions.Analyze) throw new ArgumentException($"{collected.WorklistOptions[0]} is only valid with analyze");
        if (request.Unreported == UnreportedMode.Ignore && command != CliOptions.Analyze) throw new ArgumentException("--unreported ignore is only valid with analyze");
        if (collected.Results.Count > 0 && collected.Reports.Count == 0) throw new ArgumentException("--test-results needs --reports");
        if (request.Base is not null && request.DiffFile is not null) throw new ArgumentException("--base and --diff-file exclude each other");
        if (collected.Reports.Count > 0 && (request.NoBuild || request.TestTarget is not null || request.Configuration is not null || collected.TestArgs.Count > 0))
            throw new ArgumentException("--reports reads existing coverage; --test, --configuration, --no-build and TEST_ARGS apply only when tests run");
        return new CliOptions(command, request with
        {
            Paths = collected.Paths, ListedFiles = collected.Listed, Reports = collected.Reports, TestResults = collected.Results,
            PathMaps = collected.Maps, TestArgs = collected.TestArgs,
        }, format, output);
    }

    static (string, string) PathMap(string value)
    {
        var separator = value.IndexOf('=');
        if (separator <= 0 || separator == value.Length - 1) throw new ArgumentException("--path-map needs FROM=TO");
        return (value[..separator], value[(separator + 1)..]);
    }

    static string OneOf(string value, IReadOnlyList<string> allowed, string message) => allowed.Contains(value) ? value : throw new ArgumentException(message);

    static int NonNegative(string value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) && number >= 0 ? number
            : throw new ArgumentException("--top must be a non-negative integer");

    static bool TryThresholds(string value, out double? line, out double? branch)
    {
        line = branch = null;
        var parts = value.Split(',');
        if (parts.Length > 2 || !TryPercent(parts[0], out line)) return false;
        if (parts.Length == 2 && !TryPercent(parts[1], out branch)) return false;
        return line is not null || branch is not null;
    }

    static bool TryPercent(string text, out double? value)
    {
        value = null;
        if (text.Length == 0) return true;
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) || number is < 0 or > 100) return false;
        value = number;
        return true;
    }

    static IEnumerable<string> ReadLines(TextReader reader)
    {
        while (reader.ReadLine() is { } line) yield return line;
    }
}
