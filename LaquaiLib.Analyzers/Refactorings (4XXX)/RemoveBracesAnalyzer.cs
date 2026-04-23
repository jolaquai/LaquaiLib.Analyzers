namespace LaquaiLib.Analyzers.Refactorings__4XXX_;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class RemoveBracesAnalyzer : DiagnosticAnalyzer
{
    public static DiagnosticDescriptor Descriptor { get; } = new(
        id: "LAQ4002",
        title: "Single-statement block can have its braces removed",
        messageFormat: "Single-statement block can have its braces removed",
        description: "Single-statement blocks in control-flow constructs can be simplified by removing braces when doing so is safe.",
        category: AnalyzerCategories.Refactorings,
        defaultSeverity: DiagnosticSeverity.Hidden,
        isEnabledByDefault: true
    );

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = [Descriptor];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeNode, SyntaxKind.Block);
    }

    private static void AnalyzeNode(SyntaxNodeAnalysisContext context)
    {
        var block = Unsafe.As<BlockSyntax>(context.Node);

        if (block.Statements.Count != 1 || !IsEligibleParent(block.Parent))
        {
            return;
        }

        if (block.Parent is IfStatementSyntax { Else: not null } && EndsWithUnmatchedIf(block.Statements[0]))
        {
            return;
        }

        if (!block.SyntaxTree.TryGetText(out var sourceText))
        {
            return;
        }

        var fullSpan = block.FullSpan;
        var startLine = sourceText.Lines.GetLineFromPosition(fullSpan.Start);
        var endLine = sourceText.Lines.GetLineFromPosition(fullSpan.End > 0 ? fullSpan.End - 1 : fullSpan.End);
        var location = Location.Create(block.SyntaxTree, TextSpan.FromBounds(startLine.Start, endLine.End));

        context.ReportDiagnostic(Diagnostic.Create(Descriptor, location));
    }

    internal static bool IsEligibleParent(SyntaxNode parent) => parent is
        IfStatementSyntax or
        ElseClauseSyntax or
        WhileStatementSyntax or
        ForStatementSyntax or
        ForEachStatementSyntax or
        UsingStatementSyntax or
        LockStatementSyntax or
        FixedStatementSyntax;

    private static bool EndsWithUnmatchedIf(StatementSyntax statement) => statement switch
    {
        IfStatementSyntax { Else: null } => true,
        IfStatementSyntax { Else: ElseClauseSyntax elseClause } => EndsWithUnmatchedIf(elseClause.Statement),
        WhileStatementSyntax whileStatement => EndsWithUnmatchedIf(whileStatement.Statement),
        ForStatementSyntax forStatement => EndsWithUnmatchedIf(forStatement.Statement),
        ForEachStatementSyntax forEachStatement => EndsWithUnmatchedIf(forEachStatement.Statement),
        UsingStatementSyntax usingStatement => EndsWithUnmatchedIf(usingStatement.Statement),
        LockStatementSyntax lockStatement => EndsWithUnmatchedIf(lockStatement.Statement),
        FixedStatementSyntax fixedStatement => EndsWithUnmatchedIf(fixedStatement.Statement),
        BlockSyntax { Statements.Count: 1 } nestedBlock => EndsWithUnmatchedIf(nestedBlock.Statements[0]),
        _ => false,
    };
}
