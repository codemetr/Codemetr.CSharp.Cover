using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Codemetr.CSharp.Cover;

/// <summary>What the syntax tells about coverage: executable lines, exclusion attributes, branch conditions.</summary>
internal static class SourceSyntax
{
    // An estimate of the lines a coverage engine reports: statement starts, body braces, expression bodies and initializers.
    public static IEnumerable<int> ExecutableLines(CompilationUnitSyntax root)
    {
        foreach (var node in root.DescendantNodes())
        {
            switch (node)
            {
                case BlockSyntax block when block.Parent is BaseMethodDeclarationSyntax or AccessorDeclarationSyntax or LocalFunctionStatementSyntax:
                    yield return Line(block.OpenBraceToken);
                    yield return Line(block.CloseBraceToken);
                    break;
                case BlockSyntax:
                case LocalFunctionStatementSyntax:
                    break;
                case GlobalStatementSyntax:
                    break;
                case StatementSyntax statement:
                    yield return Line(statement.GetFirstToken());
                    break;
                case ArrowExpressionClauseSyntax arrow:
                    yield return Line(arrow.Expression.GetFirstToken());
                    break;
                case ConstructorInitializerSyntax initializer:
                    yield return Line(initializer.ThisOrBaseKeyword);
                    break;
                case EqualsValueClauseSyntax value when value.Parent is VariableDeclaratorSyntax { Parent.Parent: FieldDeclarationSyntax } or PropertyDeclarationSyntax:
                    yield return Line(value.Value.GetFirstToken());
                    break;
            }
        }
    }

    public static List<(int Start, int End, string Attribute)> Exclusions(CompilationUnitSyntax root)
    {
        var result = new List<(int, int, string)>();
        foreach (var node in root.DescendantNodes().OfType<MemberDeclarationSyntax>().Cast<SyntaxNode>()
                     .Concat(root.DescendantNodes().OfType<AccessorDeclarationSyntax>()))
        {
            var lists = node switch
            {
                MemberDeclarationSyntax member => member.AttributeLists,
                AccessorDeclarationSyntax accessor => accessor.AttributeLists,
                _ => default,
            };
            foreach (var attribute in lists.SelectMany(list => list.Attributes))
            {
                var name = attribute.Name.ToString();
                name = name[(name.LastIndexOf('.') + 1)..];
                if (name.EndsWith("Attribute", StringComparison.Ordinal)) name = name[..^"Attribute".Length];
                if (!SourceModel.ExclusionAttributes.Contains(name)) continue;
                var span = node.GetLocation().GetLineSpan();
                result.Add((span.StartLinePosition.Line + 1, span.EndLinePosition.Line + 1, name));
            }
        }
        return result;
    }

    public static List<(int Line, string Code)> Conditions(CompilationUnitSyntax root)
    {
        var result = new List<(int, string)>();
        foreach (var node in root.DescendantNodes())
        {
            var (anchor, code) = node switch
            {
                IfStatementSyntax s => (s.IfKeyword, $"if ({s.Condition})"),
                WhileStatementSyntax s => (s.WhileKeyword, $"while ({s.Condition})"),
                DoStatementSyntax s => (s.WhileKeyword, $"while ({s.Condition})"),
                ForStatementSyntax s => (s.ForKeyword, $"for (...; {s.Condition}; ...)"),
                ForEachStatementSyntax s => (s.ForEachKeyword, $"foreach (... in {s.Expression})"),
                SwitchStatementSyntax s => (s.SwitchKeyword, $"switch ({s.Expression})"),
                ConditionalExpressionSyntax e => (e.QuestionToken, $"{e.Condition} ? ... : ..."),
                BinaryExpressionSyntax e when e.IsKind(SyntaxKind.CoalesceExpression) => (e.OperatorToken, $"{e.Left} ?? ..."),
                SwitchExpressionSyntax e => (e.SwitchKeyword, $"{e.GoverningExpression} switch {{ ... }}"),
                _ => (default(SyntaxToken), ""),
            };
            if (code.Length == 0) continue;
            var line = Line(anchor);
            if (result.All(item => item.Item1 != line)) result.Add((line, Shorten(code)));
        }
        return result;
    }

    static string Shorten(string code)
    {
        var single = string.Join(' ', code.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).Select(part => part.Trim()));
        return single.Length <= 80 ? single : single[..77] + "...";
    }

    static int Line(SyntaxToken token) => token.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
}
