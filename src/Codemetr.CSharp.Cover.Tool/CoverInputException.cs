namespace Codemetr.CSharp.Cover.Tool;

/// <summary>An input error or untrustworthy coverage evidence: exit 2 with a stable code and the next step.</summary>
internal sealed class CoverInputException(string code, string message, string? hint = null) : Exception(message)
{
    public string Code { get; } = code;
    public string? Hint { get; } = hint;
}

/// <summary>Stable input error codes.</summary>
internal static class InputErrorCodes
{
    public const string Usage = "usage";
    public const string InvalidConfig = "invalidConfig";
    public const string CoverletMissing = "coverletMissing";
    public const string WrongCoverageEngine = "wrongCoverageEngine";
    public const string MissingTestResults = "missingTestResults";
    public const string MissingTestProject = "missingTestProject";
    public const string StaleBuild = "staleBuild";
    public const string StaleCoverage = "staleCoverage";
    public const string ForeignCoverage = "foreignCoverage";
    public const string DuplicateReport = "duplicateReport";
    public const string NoTestsExecuted = "noTestsExecuted";
    public const string NoTestProjects = "noTestProjects";
    public const string TestRunCrashed = "testRunCrashed";
    public const string NoCoverageProduced = "noCoverageProduced";
    public const string BuildFailed = "buildFailed";
    public const string UnresolvedBase = "unresolvedBase";
    public const string ParseError = "parseError";
    public const string PartialBaseline = "partialBaseline";
    public const string ToolError = "toolError";
}
