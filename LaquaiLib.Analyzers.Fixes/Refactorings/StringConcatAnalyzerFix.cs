namespace LaquaiLib.Analyzers.Fixes.Refactorings;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(StringConcatAnalyzerFix)), Shared]
public sealed class StringConcatAnalyzerFix : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds { get; } = ["LAQ4001"];
    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        foreach (var diagnostic in context.Diagnostics)
        {
            var span = diagnostic.Location.SourceSpan;
            context.RegisterCodeFix(
                CodeAction.Create(
                    title: "Use interpolated string",
                    createChangedDocument: cancellationToken => ModifyDocument(context.Document, span, cancellationToken),
                    equivalenceKey: nameof(StringConcatAnalyzerFix)
                ),
                diagnostic
            );
        }

        return Task.CompletedTask;
    }

    private static async Task<Document> ModifyDocument(Document document, TextSpan span, CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);

        if (root?.FindNode(span, getInnermostNodeForTie: true) is not BinaryExpressionSyntax node || semanticModel is null)
        {
            return document;
        }

        while (node.Parent is BinaryExpressionSyntax { RawKind: (int)SyntaxKind.AddExpression } parent)
        {
            node = parent;
        }

        var parts = new List<ExpressionSyntax>();
        Flatten(node, parts, semanticModel, cancellationToken);

        var verbatim = ShouldUseVerbatim(parts);
        if (AllAreStringLiterals(parts))
        {
            var merged = BuildStringLiteral(parts, verbatim).WithTriviaFrom(node);
            return document.WithSyntaxRoot(root.ReplaceNode(node, merged));
        }

        var contents = new List<InterpolatedStringContentSyntax>(parts.Count);
        foreach (var part in parts)
        {
            AddPart(part, contents, verbatim);
        }

        var startKind = verbatim
            ? SyntaxKind.InterpolatedVerbatimStringStartToken
            : SyntaxKind.InterpolatedStringStartToken;

        var interpolated = SyntaxFactory.InterpolatedStringExpression(
                SyntaxFactory.Token(startKind),
                SyntaxFactory.List(contents),
                SyntaxFactory.Token(SyntaxKind.InterpolatedStringEndToken)
            )
            .WithTriviaFrom(node);

        return document.WithSyntaxRoot(root.ReplaceNode(node, interpolated));
    }

    private static bool AllAreStringLiterals(List<ExpressionSyntax> parts)
    {
        foreach (var part in parts)
        {
            if (part is not LiteralExpressionSyntax literal || !literal.IsKind(SyntaxKind.StringLiteralExpression))
            {
                return false;
            }
        }

        return parts.Count > 0;
    }

    private static LiteralExpressionSyntax BuildStringLiteral(List<ExpressionSyntax> parts, bool verbatim)
    {
        var builder = new System.Text.StringBuilder();
        foreach (var part in parts)
        {
            builder.Append(((LiteralExpressionSyntax)part).Token.ValueText);
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

    private static void Flatten(ExpressionSyntax expression, List<ExpressionSyntax> parts, SemanticModel semanticModel, CancellationToken cancellationToken)
    {
        var inner = Unparen(expression);
        if (inner is BinaryExpressionSyntax { RawKind: (int)SyntaxKind.AddExpression } binary
            && semanticModel.GetTypeInfo(binary, cancellationToken).Type?.SpecialType == SpecialType.System_String)
        {
            Flatten(binary.Left, parts, semanticModel, cancellationToken);
            Flatten(binary.Right, parts, semanticModel, cancellationToken);
            return;
        }

        parts.Add(inner);
    }

    private static bool ShouldUseVerbatim(List<ExpressionSyntax> parts)
    {
        var anyVerbatim = false;
        foreach (var part in parts)
        {
            if (part is LiteralExpressionSyntax literal && literal.IsKind(SyntaxKind.StringLiteralExpression))
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
            else if (part is InterpolatedStringExpressionSyntax interpolated)
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

    private static void AddPart(ExpressionSyntax expression, List<InterpolatedStringContentSyntax> contents, bool verbatim)
    {
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

    private static ExpressionSyntax Unparen(ExpressionSyntax expression)
    {
        while (expression is ParenthesizedExpressionSyntax parenthesized)
        {
            expression = parenthesized.Expression;
        }

        return expression;
    }
}
