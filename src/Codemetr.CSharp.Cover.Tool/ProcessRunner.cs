using System.Diagnostics;
using System.Text;

namespace Codemetr.CSharp.Cover.Tool;

/// <summary>Result of a child process.</summary>
internal sealed record ProcessResult(int ExitCode, string Output, string Error)
{
    public string Combined => Output + Error;

    /// <summary>The last lines of the combined output, for error messages.</summary>
    public string Tail(int lines = 15) => string.Join('\n', Combined.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0).TakeLast(lines));
}

/// <summary>Runs child processes without a shell, capturing their output.</summary>
internal static class ProcessRunner
{
    public static ProcessResult Run(string fileName, IEnumerable<string> arguments, string workingDirectory, CancellationToken cancellationToken = default)
    {
        var start = new ProcessStartInfo(fileName)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        // Stable, parseable output from the .NET CLI and MSBuild.
        start.Environment["DOTNET_CLI_UI_LANGUAGE"] = "en";
        start.Environment["DOTNET_NOLOGO"] = "1";
        start.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        start.Environment["MSBUILDDISABLENODEREUSE"] = "1";
        using var process = new Process { StartInfo = start };
        var output = new StringBuilder();
        var error = new StringBuilder();
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) lock (output) output.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) lock (error) error.AppendLine(e.Data); };
        try { process.Start(); }
        catch (System.ComponentModel.Win32Exception e) { throw new CoverInputException("toolNotFound", $"Cannot start '{fileName}': {e.Message}"); }
        process.StandardInput.Close();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        using (cancellationToken.Register(() => { try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { } }))
            process.WaitForExit();
        cancellationToken.ThrowIfCancellationRequested();
        lock (output) lock (error) return new ProcessResult(process.ExitCode, output.ToString(), error.ToString());
    }
}
