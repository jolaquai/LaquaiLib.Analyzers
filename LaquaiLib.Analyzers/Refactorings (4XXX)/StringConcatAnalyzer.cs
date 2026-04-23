using Microsoft.CodeAnalysis.Operations;

namespace LaquaiLib.Analyzers.Refactorings__4XXX_;

/// <summary>
/// Reports string concatenations that can be rewritten as interpolated strings.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class StringConcatAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    /// Describes diagnostic <c>LAQ4001</c>.
    /// </summary>
    public static DiagnosticDescriptor Descriptor { get; } = new(
        id: "LAQ4001",
        title: "Do not use string concatenation",
        messageFormat: "Use an interpolated string instead of string concatenation",
        description: "Interpolated strings are clearer than string concatenation when composing string values.",
        category: AnalyzerCategories.Refactorings,
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true
    );

    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = [Descriptor];

    /// <inheritdoc/>
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterOperationAction(AnalyzeOperation, OperationKind.Binary);
    }

    private static void AnalyzeOperation(OperationAnalysisContext context)
    {
        var operation = Unsafe.As<IBinaryOperation>(context.Operation);

        if (operation.OperatorKind != BinaryOperatorKind.Add || operation.Type?.SpecialType != SpecialType.System_String)
        {
            return;
        }

        var parent = operation.Parent;
        while (parent is IParenthesizedOperation parenthesized)
        {
            parent = parenthesized.Parent;
        }

        if (parent is IBinaryOperation { OperatorKind: BinaryOperatorKind.Add, Type.SpecialType: SpecialType.System_String })
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(Descriptor, operation.Syntax.GetLocation()));
    }
}
