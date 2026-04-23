using Microsoft.CodeAnalysis.Operations;

namespace LaquaiLib.Analyzers.Performance__0XXX_;

/// <summary>
/// Reports calls to <see cref="string.Join(string, string[])"/> and related overloads when the separator is a compile-time empty string.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class JoinWithEmptySeparatorAnalyzer : DiagnosticAnalyzer
{
    /// <summary>
    /// Describes diagnostic <c>LAQ0003</c>.
    /// </summary>
    public static DiagnosticDescriptor Descriptor { get; } = new(
        id: "LAQ0003",
        title: "Do not use string.Join with an empty separator",
        messageFormat: "Use string.Concat instead of string.Join with an empty separator",
        description: "Passing an empty separator to string.Join adds overhead with no benefit. Use string.Concat for the same result.",
        category: AnalyzerCategories.Performance,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true
    );

    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = [Descriptor];

    /// <inheritdoc/>
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterOperationAction(AnalyzeOperation, OperationKind.Invocation);
    }

    private static void AnalyzeOperation(OperationAnalysisContext context)
    {
        var invocation = Unsafe.As<IInvocationOperation>(context.Operation);
        var method = invocation.TargetMethod;
        if (method is not { IsStatic: true, Name: "Join", ContainingType.SpecialType: SpecialType.System_String })
        {
            return;
        }

        if (method.Parameters.Length != 2 || method.Parameters[0].Type.SpecialType != SpecialType.System_String)
        {
            return;
        }

        var separatorParameter = method.Parameters[0];
        var separatorArgument = invocation.Arguments.FirstOrDefault(a => SymbolEqualityComparer.Default.Equals(a.Parameter, separatorParameter));
        if (separatorArgument is null)
        {
            return;
        }

        var constant = separatorArgument.Value.ConstantValue;
        if (!constant.HasValue || constant.Value is not string separator || separator.Length != 0)
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(Descriptor, invocation.Syntax.GetLocation()));
    }
}
