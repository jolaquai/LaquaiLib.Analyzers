namespace LaquaiLib.Analyzers.Fixes.Refactorings;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(RemoveBracesAnalyzerFix)), Shared]
public sealed class RemoveBracesAnalyzerFix : LaquaiLibNodeFixer
{
    public RemoveBracesAnalyzerFix() : base("LAQ4002") { }

    public override FixInfo GetFixInfo(CompilationUnitSyntax compilationUnitSyntax, SyntaxNode syntaxNode, Diagnostic diagnostic)
    {
        var block = syntaxNode as BlockSyntax ?? syntaxNode.FirstAncestorOrSelf<BlockSyntax>();
        if (block is null || block.Statements.Count != 1 || !IsEligibleParent(block.Parent))
        {
            return FixInfo.Empty;
        }

        var inner = block.Statements[0];
        if (inner is LocalDeclarationStatementSyntax or LocalFunctionStatementSyntax or LabeledStatementSyntax or BlockSyntax)
        {
            return FixInfo.Empty;
        }

        var leadingTrivia = inner.GetLeadingTrivia();
        if (HasSignificantTrivia(block.OpenBraceToken.LeadingTrivia))
        {
            leadingTrivia = block.OpenBraceToken.LeadingTrivia.AddRange(leadingTrivia);
        }

        var replacement = inner
            .WithLeadingTrivia(leadingTrivia)
            .WithTrailingTrivia(inner.GetTrailingTrivia().AddRange(block.CloseBraceToken.TrailingTrivia));

        return new FixInfo("Remove braces", editor =>
        {
            editor.ReplaceNode(block, replacement);
            return default;
        });
    }

    private static bool IsEligibleParent(SyntaxNode parent) => parent is
        IfStatementSyntax or
        ElseClauseSyntax or
        WhileStatementSyntax or
        ForStatementSyntax or
        ForEachStatementSyntax or
        UsingStatementSyntax or
        LockStatementSyntax or
        FixedStatementSyntax;

    private static bool HasSignificantTrivia(SyntaxTriviaList trivia)
        => trivia.Any(static t => !t.IsKind(SyntaxKind.WhitespaceTrivia) && !t.IsKind(SyntaxKind.EndOfLineTrivia));
}
