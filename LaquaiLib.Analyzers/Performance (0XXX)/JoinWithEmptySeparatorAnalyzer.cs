namespace LaquaiLib.Analyzers.Performance__0XXX_;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class JoinWithEmptySeparatorAnalyzer : DiagnosticAnalyzer
{
    public static DiagnosticDescriptor Descriptor { get; } = new(
        id: "LAQ0003",
        title: "Do not use string.Join with an empty separator",
        messageFormat: "Use string.Concat instead of string.Join with an empty separator",
        description: "Passing an empty separator to string.Join adds overhead with no benefit. Use string.Concat for the same result.",
        category: AnalyzerCategories.Performance,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true
    );

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = [Descriptor];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeNode, SyntaxKind.InvocationExpression);
    }

    private static void AnalyzeNode(SyntaxNodeAnalysisContext context)
    {
        var invocation = Unsafe.As<InvocationExpressionSyntax>(context.Node);

        var nameText = invocation.Expression switch
        {
            MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.ValueText,
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
            _ => null,
        };
        if (nameText != "Join")
        {
            return;
        }

        var semanticModel = context.SemanticModel;
        var cancellationToken = context.CancellationToken;

        if (semanticModel.GetSymbolInfo(invocation, cancellationToken).Symbol is not IMethodSymbol method
            || !method.IsStatic
            || method.ContainingType?.SpecialType != SpecialType.System_String)
        {
            return;
        }

        if (method.Parameters.Length != 2 || method.Parameters[0].Type.SpecialType != SpecialType.System_String)
        {
            return;
        }

        var arguments = invocation.ArgumentList.Arguments;
        if (arguments.Count == 0)
        {
            return;
        }

        var constant = semanticModel.GetConstantValue(arguments[0].Expression, cancellationToken);
        if (!constant.HasValue || constant.Value is not string separator || separator.Length != 0)
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(Descriptor, invocation.GetLocation()));
    }
}
