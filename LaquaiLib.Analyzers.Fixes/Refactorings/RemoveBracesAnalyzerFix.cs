namespace LaquaiLib.Analyzers.Fixes.Refactorings;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(RemoveBracesAnalyzerFix)), Shared]
public sealed class RemoveBracesAnalyzerFix : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds { get; } = ["LAQ4002"];
    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        foreach (var diagnostic in context.Diagnostics)
        {
            var span = diagnostic.Location.SourceSpan;
            context.RegisterCodeFix(
                CodeAction.Create(
                    title: "Remove braces",
                    createChangedDocument: cancellationToken => RemoveBracesAsync(context.Document, span, cancellationToken),
                    equivalenceKey: nameof(RemoveBracesAnalyzerFix)
                ),
                diagnostic
            );
        }

        return Task.CompletedTask;
    }

    private static async Task<Document> RemoveBracesAsync(Document document, TextSpan span, CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root is null)
        {
            return document;
        }

        var node = root.FindNode(span, getInnermostNodeForTie: true);
        var block = node.FirstAncestorOrSelf<BlockSyntax>();
        if (block is null || block.Statements.Count != 1)
        {
            return document;
        }

        var statement = block.Statements[0];
        var newStatement = statement.WithTrailingTrivia(block.CloseBraceToken.TrailingTrivia);

        return document.WithSyntaxRoot(root.ReplaceNode(block, newStatement));
    }
}
