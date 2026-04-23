namespace LaquaiLib.Analyzers.Fixes.Performance;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(JoinWithEmptySeparatorAnalyzerFix)), Shared]
public sealed class JoinWithEmptySeparatorAnalyzerFix : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds { get; } = ["LAQ0003"];
    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        foreach (var diagnostic in context.Diagnostics)
        {
            var span = diagnostic.Location.SourceSpan;
            context.RegisterCodeFix(
                CodeAction.Create(
                    title: "Use string.Concat",
                    createChangedDocument: cancellationToken => ModifyDocument(context.Document, span, cancellationToken),
                    equivalenceKey: nameof(JoinWithEmptySeparatorAnalyzerFix)
                ),
                diagnostic
            );
        }

        return Task.CompletedTask;
    }

    private static async Task<Document> ModifyDocument(Document document, TextSpan span, CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is null)
        {
            return document;
        }

        var node = root.FindNode(span, getInnermostNodeForTie: true);
        while (node is not null and not InvocationExpressionSyntax)
        {
            node = node.Parent;
        }

        if (node is not InvocationExpressionSyntax invocation)
        {
            return document;
        }

        ExpressionSyntax newExpression = invocation.Expression switch
        {
            MemberAccessExpressionSyntax memberAccess => memberAccess.WithName(SyntaxFactory.IdentifierName("Concat").WithTriviaFrom(memberAccess.Name)),
            IdentifierNameSyntax identifier => SyntaxFactory.IdentifierName("Concat").WithTriviaFrom(identifier),
            _ => invocation.Expression,
        };

        var originalArguments = invocation.ArgumentList.Arguments;
        var newArguments = originalArguments.RemoveAt(0);
        if (newArguments.Count > 0)
        {
            var firstArgument = newArguments[0];
            newArguments = newArguments.Replace(firstArgument, firstArgument.WithLeadingTrivia(originalArguments[0].GetLeadingTrivia()));
        }

        var newInvocation = invocation
            .WithExpression(newExpression)
            .WithArgumentList(invocation.ArgumentList.WithArguments(newArguments));

        return document.WithSyntaxRoot(root.ReplaceNode(invocation, newInvocation));
    }
}
