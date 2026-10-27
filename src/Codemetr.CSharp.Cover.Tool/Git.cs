namespace Codemetr.CSharp.Cover.Tool;

/// <summary>The few git queries the tool needs; paths come back relative to the root.</summary>
internal sealed class Git(string root)
{
    public bool IsRepository => Run("rev-parse", "--is-inside-work-tree") is { ExitCode: 0 } result && result.Output.Trim() == "true";

    public string? Head() => Run("rev-parse", "--short", "HEAD") is { ExitCode: 0 } result ? result.Output.Trim() : null;

    public bool Dirty() => Run("status", "--porcelain", "--untracked-files=normal", "--", ".") is { ExitCode: 0 } result && result.Output.Trim().Length > 0;

    /// <summary>merge-base(REF, HEAD), or null when either cannot be resolved.</summary>
    public string? MergeBase(string reference)
    {
        if (reference == "HEAD") return Head();
        if (Run("rev-parse", "--verify", "--quiet", reference + "^{commit}") is not { ExitCode: 0 }) return null;
        return Run("merge-base", reference, "HEAD") is { ExitCode: 0 } result ? Short(result.Output.Trim()) : null;
    }

    /// <summary>Diff of the working tree against <paramref name="commit"/>, limited to and relative to the root, plus untracked files as wholly added.</summary>
    public string Diff(string commit)
    {
        var diff = Run("-c", "core.quotePath=false", "diff", "--relative", "--no-color", "--no-ext-diff", "--no-renames", "-U0", commit, "--");
        if (diff.ExitCode != 0) throw new CoverInputException(InputErrorCodes.UnresolvedBase, $"git diff against {commit} failed: {diff.Tail(3)}");
        var untracked = Run("-c", "core.quotePath=false", "ls-files", "--others", "--exclude-standard", "--", ".");
        var text = new System.Text.StringBuilder(diff.Output);
        foreach (var file in untracked.Output.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)))
        {
            var full = Path.Combine(root, file);
            if (!File.Exists(full)) continue;
            var lines = File.ReadAllLines(full).Length;
            if (lines == 0) continue;
            text.Append("+++ b/").Append(file).Append('\n').Append("@@ -0,0 +1,").Append(lines).Append(" @@\n");
            for (var i = 0; i < lines; i++) text.Append("+\n");
        }
        return text.ToString();
    }

    string Short(string sha) => Run("rev-parse", "--short", sha) is { ExitCode: 0 } result ? result.Output.Trim() : sha;

    ProcessResult Run(params string[] arguments)
    {
        try { return ProcessRunner.Run("git", arguments, root); }
        catch (CoverInputException) { return new ProcessResult(127, "", "git not found"); }
    }
}
