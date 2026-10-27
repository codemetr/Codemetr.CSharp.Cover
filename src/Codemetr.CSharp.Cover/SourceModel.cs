using Codemetr.CSharp;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Codemetr.CSharp.Cover;

/// <summary>An executable member with the identity used by codemetr-csharp (<c>Save(Order)</c>, <c>.ctor(int)</c>, <c>Total.get</c>).</summary>
/// <param name="Type">Full name of the declaring type.</param>
/// <param name="Name">Member name with parameter types.</param>
/// <param name="File">Root-relative path.</param>
/// <param name="Line">First line of the declaration.</param>
/// <param name="EndLine">Last line of the declaration.</param>
/// <param name="Complexity">Cyclomatic complexity as measured by codemetr-csharp.</param>
/// <param name="CodeLines">Lines estimated to hold executable code, used where no coverage data exists.</param>
/// <param name="ExcludedBy">The exclusion attribute that applies to this member, or null.</param>
/// <param name="Kind">method, constructor, accessor, localFunction, ... as in codemetr-csharp.</param>
public sealed record SourceMember(string Type, string Name, string File, int Line, int EndLine, int Complexity,
    IReadOnlyList<int> CodeLines, string? ExcludedBy, string Kind = "method")
{
    /// <summary>Stable identity across runs: type and member.</summary>
    public string Key => Type + "::" + Name;
}

/// <summary>A type declaration (each partial part separately).</summary>
public sealed record SourceType(string FullName, string File, int Line, int EndLine, string? ExcludedBy);

/// <summary>A measured source file: its types, members and estimated executable lines.</summary>
public sealed class SourceFile
{
    readonly int[] memberAt;
    readonly int[] typeAt;

    internal SourceFile(string path, IReadOnlyList<SourceType> types, IReadOnlyList<SourceMember> members, IReadOnlyList<int> codeLines,
        string[] text, IReadOnlyList<(int Line, string Code)> conditions)
    {
        Path = path;
        Types = types;
        Members = members;
        CodeLines = codeLines;
        Text = text;
        Conditions = conditions.ToDictionary(c => c.Line, c => c.Code);
        memberAt = Innermost(text.Length, members.Select(m => (m.Line, m.EndLine)).ToArray());
        typeAt = Innermost(text.Length, types.Select(t => (t.Line, t.EndLine)).ToArray());
    }

    /// <summary>Root-relative path.</summary>
    public string Path { get; }
    /// <summary>Type declarations in source order.</summary>
    public IReadOnlyList<SourceType> Types { get; }
    /// <summary>Executable members in source order.</summary>
    public IReadOnlyList<SourceMember> Members { get; }
    /// <summary>Every line estimated to hold executable code, including field and property initializers.</summary>
    public IReadOnlyList<int> CodeLines { get; }
    /// <summary>Source lines, 1-based access through <see cref="LineText"/>.</summary>
    public string[] Text { get; }
    /// <summary>Condition source per line of a branching statement, for findings that name an untaken branch.</summary>
    public IReadOnlyDictionary<int, string> Conditions { get; }

    /// <summary>The innermost member whose declaration contains <paramref name="line"/>, or null.</summary>
    public SourceMember? MemberAt(int line) => MemberIndexAt(line) is var index and >= 0 ? Members[index] : null;

    /// <summary>Index into <see cref="Members"/> of the innermost member containing <paramref name="line"/>, or -1.</summary>
    public int MemberIndexAt(int line) => line >= 1 && line <= memberAt.Length ? memberAt[line - 1] : -1;

    /// <summary>The innermost type declaration containing <paramref name="line"/>, or null.</summary>
    public SourceType? TypeAt(int line) => line >= 1 && line <= typeAt.Length && typeAt[line - 1] >= 0 ? Types[typeAt[line - 1]] : null;

    /// <summary>Trimmed text of a 1-based line.</summary>
    public string LineText(int line) => line >= 1 && line <= Text.Length ? Text[line - 1].Trim() : "";

    // Spans nest (local functions in methods, nested types in types): the smallest containing span wins.
    static int[] Innermost(int lines, (int Start, int End)[] spans)
    {
        var owner = Enumerable.Repeat(-1, lines).ToArray();
        var order = Enumerable.Range(0, spans.Length).OrderByDescending(i => spans[i].End - spans[i].Start).ThenBy(i => spans[i].Start);
        foreach (var index in order)
            for (var line = Math.Max(1, spans[index].Start); line <= Math.Min(lines, spans[index].End); line++) owner[line - 1] = index;
        return owner;
    }
}

/// <summary>Builds <see cref="SourceFile"/>s: identity and complexity from codemetr-csharp, executable lines from the syntax.</summary>
public static class SourceModel
{
    /// <summary>Attributes that remove code from coverage, as Coverlet treats them by default.</summary>
    public static readonly IReadOnlyList<string> ExclusionAttributes = ["ExcludeFromCodeCoverage", "GeneratedCode", "CompilerGenerated"];

    /// <summary>Analyses the documents; documents with syntax errors are returned as parse errors and not modelled.</summary>
    public static (IReadOnlyList<SourceFile> Files, IReadOnlyList<ParseError> ParseErrors) Build(IReadOnlyList<SourceDocument> documents,
        CancellationToken cancellationToken = default)
    {
        var analysis = CodeAnalyzer.Analyze(documents, cancellationToken: cancellationToken);
        var failed = analysis.ParseErrors.Select(e => e.File).ToHashSet(StringComparer.Ordinal);
        var typesByFile = analysis.Types.ToLookup(t => t.File, StringComparer.Ordinal);
        var files = new List<SourceFile>();
        foreach (var document in documents)
        {
            if (failed.Contains(document.Path)) continue;
            var tree = CSharpSyntaxTree.ParseText(document.Text, CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview),
                document.Path, cancellationToken: cancellationToken);
            files.Add(Model(document, (CompilationUnitSyntax)tree.GetRoot(cancellationToken), typesByFile[document.Path].ToArray()));
        }
        return (files, analysis.ParseErrors);
    }

    static SourceFile Model(SourceDocument document, CompilationUnitSyntax root, TypeMetrics[] measured)
    {
        var text = document.Text.Split('\n').Select(line => line.TrimEnd('\r')).ToArray();
        var codeLines = new SortedSet<int>(SourceSyntax.ExecutableLines(root));
        var exclusions = SourceSyntax.Exclusions(root);
        string? ExcludedBy(int line) => exclusions.Where(e => line >= e.Start && line <= e.End).Select(e => e.Attribute).FirstOrDefault();

        var types = measured.Select(t => new SourceType(t.FullName, document.Path, t.Line, t.Line + t.Lines - 1, ExcludedBy(t.Line))).ToArray();
        var spans = measured.SelectMany(t => t.Members.Where(m => (m.File ?? t.File) == document.Path).Select(m => (Type: t, Member: m))).ToArray();
        var members = new List<SourceMember>();
        foreach (var (type, member) in spans)
        {
            var end = member.Line + member.Lines - 1;
            members.Add(new SourceMember(type.FullName, member.Name, document.Path, member.Line, end, member.CyclomaticComplexity, [],
                ExcludedBy(member.Line) ?? ExcludedBy(type.Line), member.Kind));
        }
        // A line belongs to the innermost member only (a local function's lines are not its outer method's).
        var draft = new SourceFile(document.Path, types, members, codeLines.ToArray(), text, []);
        var linesByMember = codeLines.Select(line => (Line: line, Index: draft.MemberIndexAt(line))).Where(item => item.Index >= 0)
            .GroupBy(item => item.Index).ToDictionary(group => group.Key, group => group.Select(item => item.Line).ToArray());
        var final = members.Select((m, index) => m with { CodeLines = linesByMember.GetValueOrDefault(index) ?? [] }).ToArray();
        return new SourceFile(document.Path, types, final, codeLines.ToArray(), text, SourceSyntax.Conditions(root));
    }
}
