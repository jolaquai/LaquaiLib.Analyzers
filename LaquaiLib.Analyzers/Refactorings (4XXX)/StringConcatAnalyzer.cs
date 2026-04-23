namespace LaquaiLib.Analyzers.Refactorings__4XXX_;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class StringConcatAnalyzer : DiagnosticAnalyzer
{
    public static DiagnosticDescriptor Descriptor { get; } = new(
        id: "LAQ4001",
        title: "Do not use string concatenation",
        messageFormat: "Use an interpolated string instead of string concatenation",
        description: "Interpolated strings are clearer than string concatenation when composing string values.",
        category: AnalyzerCategories.Refactorings,
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true
    );

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = [Descriptor];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeNode, SyntaxKind.AddExpression);
    }

    private static void AnalyzeNode(SyntaxNodeAnalysisContext context)
    {
        var node = Unsafe.As<BinaryExpressionSyntax>(context.Node);

        if (node.Parent is BinaryExpressionSyntax { RawKind: (int)SyntaxKind.AddExpression })
        {
            return;
        }

        if (context.SemanticModel.GetTypeInfo(node, context.CancellationToken).Type?.SpecialType != SpecialType.System_String)
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(Descriptor, node.GetLocation()));
    }
}
