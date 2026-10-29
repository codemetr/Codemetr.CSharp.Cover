using System.Text.Json;
using System.Text.Json.Serialization;
using Codemetr.CSharp.Cover;

namespace Codemetr.CSharp.Cover.Tool;

/// <summary>Process exit codes, as in codemetr-csharp: 0 passed, 1 findings, 2 input or usage error.</summary>
internal static class ExitCodes
{
    public const int Passed = 0;
    public const int Findings = 1;
    public const int InputError = 2;
}

internal static class Program
{
    static readonly JsonSerializerOptions Json = new(Codemetr.CSharp.CodemetrJson.Options) { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    public static async Task<int> Main(string[] args) => await RunAsync(args, Console.In, Console.Out, Console.Error);

    public static async Task<int> RunAsync(string[] args, TextReader stdin, TextWriter stdout, TextWriter stderr)
    {
        if (args is ["--version"]) { await stdout.WriteLineAsync(CoverService.Version); return ExitCodes.Passed; }
        if (args is [] or ["--help"] or ["-h"]) { await stdout.WriteLineAsync(CliOptions.Usage); return args.Length == 0 ? ExitCodes.InputError : ExitCodes.Passed; }
        CliOptions options;
        try { options = CliOptions.Parse(args, stdin); }
        catch (Exception e) when (e is ArgumentException or IOException or UnauthorizedAccessException)
        {
            await stderr.WriteLineAsync("codemetr-csharp-cover: " + e.Message);
            await stderr.WriteLineAsync("Run 'codemetr-csharp-cover --help' for usage.");
            return ExitCodes.InputError;
        }
        // A guard must never report success by crashing: every failure is an input or tool error.
        try
        {
            return options.Command == CliOptions.Doctor ? await DoctorCommand.RunAsync(options, stdout, Json) : await ExecuteAsync(options, stdout, stderr);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            await WriteInputError(options, Describe(e), stdout, stderr);
            return ExitCodes.InputError;
        }
    }

    /// <summary>Input problems keep their code and message; anything else is a tool error that names its exception type.</summary>
    internal static InputError Describe(Exception failure)
    {
        var cause = failure is AggregateException aggregate ? aggregate.Flatten().InnerExceptions[0] : failure;
        return cause switch
        {
            CoverInputException input => new InputError(input.Code, input.Message, input.Hint),
            InvalidDataException or JsonException => new InputError(InputErrorCodes.InvalidConfig, cause.Message, null),
            IOException or UnauthorizedAccessException or ArgumentException => new InputError(InputErrorCodes.Usage, cause.Message, null),
            _ => new InputError(InputErrorCodes.ToolError, $"unexpected {cause.GetType().Name}: {cause.Message}", null),
        };
    }

    static async Task<int> ExecuteAsync(CliOptions options, TextWriter stdout, TextWriter stderr)
    {
        var state = CoverService.Run(options.Command, options.Request);
        var report = ReportBuilder.Build(state, complete: false);
        if (options.Output is not null)
        {
            var output = Path.GetFullPath(options.Output, Path.GetFullPath(options.Request.Root));
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            await File.WriteAllTextAsync(output, JsonSerializer.Serialize(ReportBuilder.Build(state, complete: true), Json) + "\n");
        }
        switch (options.Format)
        {
            case CliOptions.Text: TextReport.Write(report, stdout); break;
            case CliOptions.Github: GithubReport.Write(report, stdout); break;
            default:
                // JSON keeps stdout machine-readable and repeats warnings on stderr.
                foreach (var warning in report.Warnings) await stderr.WriteLineAsync("codemetr-csharp-cover: warning: " + warning);
                await stdout.WriteLineAsync(JsonSerializer.Serialize(report, Json));
                break;
        }
        if (report.ParseErrors.Count > 0) return ExitCodes.InputError;
        return options.Command != CliOptions.Analyze && !report.Passed ? ExitCodes.Findings : ExitCodes.Passed;
    }

    // Input errors keep the report contract: JSON on stdout for machines, the message on stderr for people.
    static async Task WriteInputError(CliOptions options, InputError error, TextWriter stdout, TextWriter stderr)
    {
        await stderr.WriteLineAsync($"codemetr-csharp-cover: [{error.Code}] {error.Message}");
        if (error.Hint is not null) await stderr.WriteLineAsync("hint: " + error.Hint);
        if (options.Format != CliOptions.Json) return;
        await stdout.WriteLineAsync(JsonSerializer.Serialize(new CoverReport
        {
            Version = CoverService.Version, Command = options.Command, Passed = false, Complete = false, InputErrors = [error],
        }, Json));
    }
}
