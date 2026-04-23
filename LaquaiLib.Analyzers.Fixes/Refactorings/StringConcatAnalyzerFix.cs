using Microsoft.CodeAnalysis.Operations;

namespace LaquaiLib.Analyzers.Fixes.Refactorings;

/// <summary>
/// Provides the code fix for <c>LAQ4001</c>.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(StringConcatAnalyzerFix)), Shared]
public sealed class StringConcatAnalyzerFix : LaquaiLibNodeFixer
{
    /// <summary>
    /// Initializes a new instance of the <see cref="StringConcatAnalyzerFix"/> class.
    /// </summary>
    public StringConcatAnalyzerFix() : base("LAQ4001") { }

    /// <inheritdoc/>
    public override FixInfo GetFixInfo(CompilationUnitSyntax compilationUnitSyntax, SyntaxNode syntaxNode, Diagnostic diagnostic)
    {
        var node = syntaxNode as BinaryExpressionSyntax ?? syntaxNode.FirstAncestorOrSelf<BinaryExpressionSyntax>();
        if (node is null || !node.IsKind(SyntaxKind.AddExpression))
        {
            return FixInfo.Empty;
        }

        while (node.Parent is BinaryExpressionSyntax { RawKind: (int)SyntaxKind.AddExpression } parent)
        {
            node = parent;
        }

        return new FixInfo("Use interpolated string", async editor =>
        {
            var semanticModel = await editor.OriginalDocument.GetSemanticModelAsync(default).ConfigureAwait(false);
            if (semanticModel?.GetOperation(node) is not IBinaryOperation operation
                || operation.OperatorKind != BinaryOperatorKind.Add
                || operation.Type?.SpecialType != SpecialType.System_String)
            {
                return;
            }

            var parts = new List<IOperation>();
            Flatten(operation, parts);

            var verbatim = ShouldUseVerbatim(parts);
            if (AllAreStringLiterals(parts))
            {
                editor.ReplaceNode(node, BuildStringLiteral(parts, verbatim).WithTriviaFrom(node));
                return;
            }

            var contents = new List<InterpolatedStringContentSyntax>(parts.Count);
            foreach (var part in parts)
            {
                AddPart(part, contents, verbatim);
            }

            var startKind = verbatim
                ? SyntaxKind.InterpolatedVerbatimStringStartToken
                : SyntaxKind.InterpolatedStringStartToken;

            editor.ReplaceNode(node, SyntaxFactory.InterpolatedStringExpression(
                    SyntaxFactory.Token(startKind),
                    SyntaxFactory.List(contents),
                    SyntaxFactory.Token(SyntaxKind.InterpolatedStringEndToken)
                )
                .WithTriviaFrom(node));
        });
    }

    private static bool AllAreStringLiterals(List<IOperation> parts)
    {
        foreach (var part in parts)
        {
            if (GetPartExpression(part) is not LiteralExpressionSyntax literal || !literal.IsKind(SyntaxKind.StringLiteralExpression))
            {
                return false;
            }
        }

        return parts.Count > 0;
    }

    private static LiteralExpressionSyntax BuildStringLiteral(List<IOperation> parts, bool verbatim)
    {
        var builder = new System.Text.StringBuilder();
        foreach (var part in parts)
        {
            builder.Append(((LiteralExpressionSyntax)GetPartExpression(part)).Token.ValueText);
        }

        var value = builder.ToString();
        var text = verbatim
            ? "@\"" + value.Replace("\"", "\"\"") + "\""
            : "\"" + SymbolDisplay.FormatLiteral(value, quote: false) + "\"";

        return SyntaxFactory.LiteralExpression(
            SyntaxKind.StringLiteralExpression,
            SyntaxFactory.Literal(default, text, value, default)
        );
    }

    private static void Flatten(IOperation operation, List<IOperation> parts)
    {
        var inner = Unwrap(operation);
        if (inner is IBinaryOperation { OperatorKind: BinaryOperatorKind.Add, Type.SpecialType: SpecialType.System_String } binary)
        {
            Flatten(binary.LeftOperand, parts);
            Flatten(binary.RightOperand, parts);
            return;
        }

        parts.Add(inner);
    }

    private static bool ShouldUseVerbatim(List<IOperation> parts)
    {
        var anyVerbatim = false;
        foreach (var part in parts)
        {
            if (GetPartExpression(part) is LiteralExpressionSyntax literal && literal.IsKind(SyntaxKind.StringLiteralExpression))
            {
                var text = literal.Token.Text;
                if (text.Length > 0 && text[0] == '@')
                {
                    anyVerbatim = true;
                }
                else if (!text.StartsWith("\"\"\"", StringComparison.Ordinal) && HasBadVerbatimChar(literal.Token.ValueText))
                {
                    return false;
                }
            }
            else if (GetPartExpression(part) is InterpolatedStringExpressionSyntax interpolated)
            {
                if (interpolated.StringStartToken.IsKind(SyntaxKind.InterpolatedVerbatimStringStartToken))
                {
                    anyVerbatim = true;
                }
                else if (interpolated.StringStartToken.IsKind(SyntaxKind.InterpolatedStringStartToken))
                {
                    foreach (var content in interpolated.Contents)
                    {
                        if (content is InterpolatedStringTextSyntax textContent && HasBadVerbatimChar(textContent.TextToken.ValueText))
                        {
                            return false;
                        }
                    }
                }
            }
        }

        return anyVerbatim;
    }

    private static bool HasBadVerbatimChar(string value)
    {
        return value.IndexOfAny(['\0', '\a', '\b', '\f', '\v', '\r', '\n', '\t']) >= 0;
    }

    private static void AddPart(IOperation operation, List<InterpolatedStringContentSyntax> contents, bool verbatim)
    {
        var expression = GetInterpolationExpression(operation);
        if (expression is null)
        {
            return;
        }

        if (expression is LiteralExpressionSyntax literal && literal.IsKind(SyntaxKind.StringLiteralExpression))
        {
            AppendText(literal.Token.ValueText, contents, verbatim);
            return;
        }

        if (expression is InterpolatedStringExpressionSyntax interpolated
            && (interpolated.StringStartToken.IsKind(SyntaxKind.InterpolatedStringStartToken)
                || interpolated.StringStartToken.IsKind(SyntaxKind.InterpolatedVerbatimStringStartToken)))
        {
            foreach (var content in interpolated.Contents)
            {
                if (content is InterpolatedStringTextSyntax textContent)
                {
                    AppendText(textContent.TextToken.ValueText, contents, verbatim);
                }
                else
                {
                    contents.Add(content);
                }
            }

            return;
        }

        contents.Add(SyntaxFactory.Interpolation(Parenthesize(expression.WithoutTrivia())));
    }

    private static ExpressionSyntax Parenthesize(ExpressionSyntax expression)
        => NeedsParens(expression) ? SyntaxFactory.ParenthesizedExpression(expression) : expression;

    private static bool NeedsParens(ExpressionSyntax expression) => expression switch
    {
        ConditionalExpressionSyntax => true,
        AssignmentExpressionSyntax => true,
        SwitchExpressionSyntax => true,
        LambdaExpressionSyntax => true,
        AnonymousMethodExpressionSyntax => true,
        QueryExpressionSyntax => true,
        IsPatternExpressionSyntax => true,
        BinaryExpressionSyntax binary when binary.IsKind(SyntaxKind.AsExpression)
            || binary.IsKind(SyntaxKind.IsExpression)
            || binary.IsKind(SyntaxKind.CoalesceExpression) => true,
        _ => false,
    };

    private static void AppendText(string value, List<InterpolatedStringContentSyntax> contents, bool verbatim)
    {
        if (value.Length == 0)
        {
            return;
        }

        var encoded = verbatim ? EncodeVerbatim(value) : EncodeRegular(value);
        var lastIndex = contents.Count - 1;
        if (lastIndex >= 0 && contents[lastIndex] is InterpolatedStringTextSyntax last)
        {
            var combinedValue = last.TextToken.ValueText + value;
            var combinedText = last.TextToken.Text + encoded;
            contents[lastIndex] = SyntaxFactory.InterpolatedStringText(
                SyntaxFactory.Token(default, SyntaxKind.InterpolatedStringTextToken, combinedText, combinedValue, default)
            );
            return;
        }

        contents.Add(SyntaxFactory.InterpolatedStringText(
            SyntaxFactory.Token(default, SyntaxKind.InterpolatedStringTextToken, encoded, value, default)
        ));
    }

    private static string EncodeRegular(string value)
    {
        var encoded = SymbolDisplay.FormatLiteral(value, quote: false);
        if (encoded.IndexOfAny(['{', '}']) >= 0)
        {
            encoded = encoded.Replace("{", "{{").Replace("}", "}}");
        }

        return encoded;
    }

    private static string EncodeVerbatim(string value)
        => value.Replace("\"", "\"\"").Replace("{", "{{").Replace("}", "}}");

    private static ExpressionSyntax GetInterpolationExpression(IOperation operation)
    {
        var inner = Unwrap(operation);
        if (inner is IInvocationOperation
            {
                TargetMethod: { Name: "ToString", IsStatic: false, Parameters.Length: 0, ReturnType.SpecialType: SpecialType.System_String },
                Instance: { } instance,
            })
        {
            return GetPartExpression(instance) ?? GetPartExpression(inner);
        }

        return GetPartExpression(inner);
    }

    private static ExpressionSyntax GetPartExpression(IOperation operation)
    {
        var syntax = Unwrap(operation).Syntax as ExpressionSyntax ?? Unwrap(operation).Syntax.FirstAncestorOrSelf<ExpressionSyntax>();
        return syntax is null ? null : Unparen(syntax);
    }

    private static IOperation Unwrap(IOperation operation)
    {
        while (true)
        {
            switch (operation)
            {
                case IParenthesizedOperation parenthesized:
                    operation = parenthesized.Operand;
                    continue;
                case IConversionOperation conversion when conversion.IsImplicit:
                    operation = conversion.Operand;
                    continue;
                default:
                    return operation;
            }
        }
    }

    private static ExpressionSyntax Unparen(ExpressionSyntax expression)
    {
        while (expression is ParenthesizedExpressionSyntax parenthesized)
        {
            expression = parenthesized.Expression;
        }

        return expression;
    }
}
