namespace LaquaiLib.Analyzers.Fixes.Performance;

/// <summary>
/// Provides the code fix for <c>LAQ0003</c>.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(JoinWithEmptySeparatorAnalyzerFix)), Shared]
public sealed class JoinWithEmptySeparatorAnalyzerFix : LaquaiLibNodeFixer
{
    /// <summary>
    /// Initializes a new instance of the <see cref="JoinWithEmptySeparatorAnalyzerFix"/> class.
    /// </summary>
    public JoinWithEmptySeparatorAnalyzerFix() : base("LAQ0003") { }

    /// <inheritdoc/>
    public override FixInfo GetFixInfo(CompilationUnitSyntax compilationUnitSyntax, SyntaxNode syntaxNode, Diagnostic diagnostic)
    {
        var invocation = syntaxNode as InvocationExpressionSyntax ?? syntaxNode.FirstAncestorOrSelf<InvocationExpressionSyntax>();
        if (invocation is null)
        {
            return FixInfo.Empty;
        }

        var originalArguments = invocation.ArgumentList.Arguments;
        if (originalArguments.Count < 2)
        {
            return FixInfo.Empty;
        }

        var separatorArgument = originalArguments.FirstOrDefault(static argument =>
            argument.NameColon?.Name is IdentifierNameSyntax { Identifier.ValueText: "separator" });
        separatorArgument ??= originalArguments[0];

        var separatorIndex = originalArguments.IndexOf(separatorArgument);
        if (separatorIndex < 0)
        {
            return FixInfo.Empty;
        }

        ExpressionSyntax newExpression = invocation.Expression switch
        {
            MemberAccessExpressionSyntax memberAccess => memberAccess.WithName(SyntaxFactory.IdentifierName("Concat").WithTriviaFrom(memberAccess.Name)),
            IdentifierNameSyntax identifier => SyntaxFactory.IdentifierName("Concat").WithTriviaFrom(identifier),
            _ => invocation.Expression,
        };

        var newArguments = originalArguments.RemoveAt(separatorIndex);
        if (newArguments.Count > separatorIndex)
        {
            var successorArgument = newArguments[separatorIndex];
            newArguments = newArguments.Replace(successorArgument, successorArgument.WithLeadingTrivia(separatorArgument.GetLeadingTrivia()));
        }
        else if (newArguments.Count > 0)
        {
            var predecessorArgument = newArguments[newArguments.Count - 1];
            newArguments = newArguments.Replace(predecessorArgument, predecessorArgument.WithTrailingTrivia(predecessorArgument.GetTrailingTrivia().AddRange(separatorArgument.GetLeadingTrivia())));
        }

        var newInvocation = invocation
            .WithExpression(newExpression)
            .WithArgumentList(invocation.ArgumentList.WithArguments(newArguments));

        return new FixInfo("Use string.Concat", editor =>
        {
            editor.ReplaceNode(invocation, newInvocation);
            return default;
        });
    }
}
