using System.Text.Json;
using System.Text.Json.Serialization;

namespace Codemetr.CSharp.Cover;

/// <summary>Coverage floor of a member: later runs may not fall below it.</summary>
public sealed record MemberFloor(string Type, string Member, string File, double Line, double? Branch)
{
    /// <summary>Identity across runs.</summary>
    [JsonIgnore] public string Key => Type + "::" + Member;
}

/// <summary>A tolerated violation of a baselinable rule.</summary>
public sealed record DebtEntry(string Rule, string Type, string? Member, string File, double Value)
{
    /// <summary>Identity: rule, type and member (the file may change when code moves).</summary>
    [JsonIgnore] public string Key => Rule + "|" + Type + "|" + Member;
}

/// <summary>Skipped tests recorded for one test project.</summary>
public sealed record SkipEntry(string Project, int Skipped);

/// <summary>An accepted exclusion: an attribute on code, or a configuration glob.</summary>
public sealed record ExclusionEntry(string Kind, string? Type, string? Member, string? Pattern)
{
    /// <summary>Exclusion attribute on a type or member.</summary>
    public const string Attribute = "attribute";
    /// <summary>Configuration exclude glob.</summary>
    public const string Glob = "glob";

    /// <summary>Identity.</summary>
    [JsonIgnore] public string Key => Kind + "|" + Type + "|" + Member + "|" + Pattern;
}

/// <summary>When and with which thresholds the baseline was recorded.</summary>
public sealed record BaselineOrigin(string? Head, IReadOnlyDictionary<string, double> Thresholds);

/// <summary>
/// Contents of <c>codemetr-cover.baseline.json</c>: per-member floors, tolerated debt, skip counts and
/// the exclusion inventory. Once it exists it is only tightened: floors rise, debt and exclusions shrink.
/// </summary>
public sealed record CoverBaseline(int Schema, BaselineOrigin RecordedAt, MemberFloor[] Members,
    DebtEntry[] Debt, SkipEntry[] Tests, ExclusionEntry[] Exclusions)
{
    /// <summary>Conventional file name in the root.</summary>
    public const string FileName = "codemetr-cover.baseline.json";
    /// <summary>Current file format.</summary>
    public const int CurrentSchema = 1;

    /// <summary>Loads and validates a baseline file.</summary>
    public static CoverBaseline Load(string path)
    {
        CoverBaseline? baseline;
        try { baseline = JsonSerializer.Deserialize<CoverBaseline>(File.ReadAllBytes(path), Codemetr.CSharp.CodemetrJson.Options); }
        catch (JsonException e) { throw new InvalidDataException($"Invalid {FileName}: {e.Message}"); }
        if (baseline is null || baseline.Schema != CurrentSchema)
            throw new InvalidDataException($"Unsupported {FileName} schema {baseline?.Schema}");
        if (baseline.RecordedAt?.Thresholds is null || baseline.Members is null || baseline.Debt is null
            || baseline.Tests is null || baseline.Exclusions is null)
            throw new InvalidDataException($"Incomplete {FileName}");
        return baseline;
    }

    /// <summary>Writes the file with sorted entries, so its diff in review shows exactly what changed.</summary>
    public void Save(string path) => File.WriteAllText(path, JsonSerializer.Serialize(Sorted(), Codemetr.CSharp.CodemetrJson.Options) + "\n");

    CoverBaseline Sorted() => this with
    {
        Members = Members.OrderBy(m => m.File, StringComparer.Ordinal).ThenBy(m => m.Key, StringComparer.Ordinal).ToArray(),
        Debt = Debt.OrderBy(d => d.File, StringComparer.Ordinal).ThenBy(d => d.Key, StringComparer.Ordinal).ToArray(),
        Tests = Tests.OrderBy(t => t.Project, StringComparer.Ordinal).ToArray(),
        Exclusions = Exclusions.OrderBy(e => e.Key, StringComparer.Ordinal).ToArray(),
    };
}
