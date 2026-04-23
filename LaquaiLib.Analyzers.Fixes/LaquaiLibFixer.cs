using System.Text.RegularExpressions;

using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Editing;
using Microsoft.CodeAnalysis.Operations;

namespace LaquaiLib.Analyzers.Fixes;

/// <summary>
/// Provides a base class for all code fix providers in the LaquaiLib library with some shared functionality for implementing single and batch fixes in a more streamlined way.
/// </summary>
public abstract class LaquaiLibFixer(params ImmutableArray<string> fixableDiagnosticIds) : CodeFixProvider
{
    #region override
    /// <inheritdoc/>
    public sealed override ImmutableArray<string> FixableDiagnosticIds { get; } = fixableDiagnosticIds;
    /// <inheritdoc/>
    public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var document = context.Document;
        if (await document.GetRootAsync(context.CancellationToken).ConfigureAwait(false) is not CompilationUnitSyntax compilationUnitSyntax)
        {
            return;
        }

        var diagnostics = context.Diagnostics;
        for (var i = 0; i < context.Diagnostics.Length; i++)
        {
            var diagnostic = diagnostics[i];
            var fixInfo = await GetFixInfoAsync(document, compilationUnitSyntax, diagnostic, context.CancellationToken).ConfigureAwait(false);
            if (!fixInfo.HasFix)
            {
                continue;
            }

            var (title, key, fixAction) = fixInfo;

            context.RegisterCodeFix(CodeAction.Create(
                title: title,
                createChangedDocument: async c =>
                {
                    var editor = await DocumentEditor.CreateAsync(document, c).ConfigureAwait(false);
                    await fixAction(editor).ConfigureAwait(false);
                    var changed = editor.GetChangedDocument();
                    changed = await ExecutePostFixes(changed).ConfigureAwait(false);
                    return changed;
                },
                equivalenceKey: $"{Prefix}_{key}"
            ), diagnostic);
        }
    }

    /// <inheritdoc/>
    public override FixAllProvider GetFixAllProvider() => FixAllProvider.Create(FixAllAsync);
    #endregion

    private string Prefix => field ??= GetType().FullName;
    private async Task<Document> FixAllAsync(FixAllContext fixAllContext, Document document, ImmutableArray<Diagnostic> diagnostics)
    {
        if (diagnostics.IsEmpty)
        {
            return document;
        }

        if (await document.GetRootAsync(fixAllContext.CancellationToken).ConfigureAwait(false) is not CompilationUnitSyntax root)
        {
            return document;
        }
        var editor = await DocumentEditor.CreateAsync(document, fixAllContext.CancellationToken).ConfigureAwait(false);

        for (var i = 0; i < diagnostics.Length; i++)
        {
            var diagnostic = diagnostics[i];
            var fixInfo = await GetFixInfoAsync(document, root, diagnostic, fixAllContext.CancellationToken).ConfigureAwait(false);
            if (!fixInfo.HasFix)
            {
                continue;
            }
            var (title, key, fixAction) = fixInfo;
            await fixAction(editor).ConfigureAwait(false);
        }

        var changed = editor.GetChangedDocument();
        changed = await ExecutePostFixes(changed).ConfigureAwait(false);

        return changed;
    }

    private async Task<Document> ExecutePostFixes(Document changed)
    {
        if (PostFixAction?.InvocationList is var postFixes and not null)
        {
            for (var i = 0; i < postFixes.Length; i++)
            {
                var func = postFixes[i];
                changed = await func(changed).ConfigureAwait(false);
                PostFixAction -= func;
            }
        }
        PostFixAction = null;

        return changed;
    }

    /// <summary>
    /// When overridden in a derived class, provides the fix information for a specific <see cref="Diagnostic"/>.
    /// When using a more derived base class than <see cref="LaquaiLibFixer"/> itself, override that class's <c>GetFixInfo</c> instead.
    /// Returned <see cref="FixInfo.FixAction"/> must not replace the root node of the <see cref="DocumentEditor"/> they are passed (which is the <paramref name="compilationUnitSyntax"/> passed here). If that is required, use <see cref="PostFixAction"/> instead.
    /// </summary>
    /// <param name="compilationUnitSyntax">The <see cref="CompilationUnitSyntax"/> of the document.</param>
    /// <param name="diagnostic">The <see cref="Diagnostic"/> to fix.</param>
    /// <returns>A <see cref="FixInfo"/> containing the fix information.</returns>
    public abstract FixInfo GetFixInfo(CompilationUnitSyntax compilationUnitSyntax, Diagnostic diagnostic);
    /// <summary>
    /// Asynchronously resolves the <see cref="FixInfo"/> for a specific <see cref="Diagnostic"/>.
    /// </summary>
    /// <param name="document">The <see cref="Document"/> containing <paramref name="compilationUnitSyntax"/>.</param>
    /// <param name="compilationUnitSyntax">The <see cref="CompilationUnitSyntax"/> of the document.</param>
    /// <param name="diagnostic">The <see cref="Diagnostic"/> to fix.</param>
    /// <param name="cancellationToken">A <see cref="CancellationToken"/> to observe while resolving the fix.</param>
    /// <returns>A <see cref="ValueTask{TResult}"/> whose result is a <see cref="FixInfo"/> containing the fix information.</returns>
    public virtual ValueTask<FixInfo> GetFixInfoAsync(Document document, CompilationUnitSyntax compilationUnitSyntax, Diagnostic diagnostic, CancellationToken cancellationToken)
        => new(GetFixInfo(compilationUnitSyntax, diagnostic));
    /// <summary>
    /// Encapsulates methods to call after the fix has been applied (or all fixes if called from the context of the fix-all provider).
    /// Can be used to perform additional actions on the <see cref="Document"/>, for example, if they are incompatible with the <see cref="DocumentEditor"/>.
    /// They are invoked in order of registration and awaited individually. Their changes are introduced sequentially, each invocation <see langword="await"/>ed and passed the result of the previous invocation (if there are multiple).
    /// </summary>
    public event Func<Document, ValueTask<Document>> PostFixAction;

    /// <summary>
    /// Contains cached delegates for common actions intended to be wrapped for use in <see cref="PostFixAction"/>.
    /// </summary>
    public static class WellKnownPostFixActions
    {
        /// <summary>
        /// Adds the specified <see langword="using"/> directives when they are not already present in the document.
        /// </summary>
        /// <param name="document">The <see cref="Document"/> to update.</param>
        /// <param name="usings">The namespaces to add.</param>
        /// <returns>A <see cref="ValueTask{TResult}"/> whose result is the updated <see cref="Document"/>.</returns>
        public static async ValueTask<Document> AddUsingsIfNotExist(Document document, params string[] usings)
        {
            var compilationUnitSyntax = await document.Root.ConfigureAwait(false);
            var newUsings = new HashSet<string>(usings);
            newUsings.ExceptWith(compilationUnitSyntax.Usings.Select(static u => u.Name.ToString()));
            var filtered = newUsings.Select(u => SyntaxFactory.UsingDirective(SyntaxFactory.ParseName(u))).ToArray();
            return document.WithSyntaxRoot(filtered.Length == 0 ? compilationUnitSyntax : compilationUnitSyntax.AddUsings(filtered));
        }
    }
}

/// <summary>
/// Provides a base class for code fix providers for analyzers that report diagnostics on <see cref="SyntaxToken"/>s.
/// </summary>
/// <param name="fixableDiagnosticIds">An <see cref="ImmutableArray{T}"/> of fixable diagnostic IDs.</param>
public abstract class LaquaiLibTokenFixer(params ImmutableArray<string> fixableDiagnosticIds) : LaquaiLibFixer(fixableDiagnosticIds)
{
    /// <summary>
    /// When overridden in a derived class, provides the fix information for a specific <see cref="Diagnostic"/>.
    /// </summary>
    /// <param name="compilationUnitSyntax">The <see cref="CompilationUnitSyntax"/> of the document.</param>
    /// <param name="syntaxToken">The <see cref="SyntaxToken"/> on which <paramref name="diagnostic"/> was reported.</param>
    /// <param name="diagnostic">The <see cref="Diagnostic"/> to fix.</param>
    /// <returns>A <see cref="FixInfo"/> containing the fix information.</returns>
    public abstract FixInfo GetFixInfo(CompilationUnitSyntax compilationUnitSyntax, SyntaxToken syntaxToken, Diagnostic diagnostic);
    /// <summary>
    /// Do not use. Override <see cref="GetFixInfo(CompilationUnitSyntax, SyntaxToken, Diagnostic)"/> instead.
    /// </summary>
    public sealed override FixInfo GetFixInfo(CompilationUnitSyntax compilationUnitSyntax, Diagnostic diagnostic)
        => GetFixInfo(compilationUnitSyntax, compilationUnitSyntax.FindToken(diagnostic.Location.SourceSpan.Start), diagnostic);
}
/// <summary>
/// Provides a base class for code fix providers for analyzers that report diagnostics on <see cref="SyntaxNode"/>s.
/// </summary>
/// <param name="fixableDiagnosticIds">An <see cref="ImmutableArray{T}"/> of fixable diagnostic IDs.</param>
public abstract class LaquaiLibNodeFixer(params ImmutableArray<string> fixableDiagnosticIds) : LaquaiLibFixer(fixableDiagnosticIds)
{
    /// <summary>
    /// When overridden in a derived class, provides the fix information for a specific <see cref="Diagnostic"/>.
    /// </summary>
    /// <param name="compilationUnitSyntax">The <see cref="CompilationUnitSyntax"/> of the document.</param>
    /// <param name="syntaxNode">The <see cref="SyntaxNode"/> on which <paramref name="diagnostic"/> was reported.</param>
    /// <param name="diagnostic">The <see cref="Diagnostic"/> to fix.</param>
    /// <returns>A <see cref="FixInfo"/> containing the fix information.</returns>
    public abstract FixInfo GetFixInfo(CompilationUnitSyntax compilationUnitSyntax, SyntaxNode syntaxNode, Diagnostic diagnostic);
    /// <summary>
    /// Do not use. Override <see cref="GetFixInfo(CompilationUnitSyntax, SyntaxNode, Diagnostic)"/> instead.
    /// </summary>
    public sealed override FixInfo GetFixInfo(CompilationUnitSyntax compilationUnitSyntax, Diagnostic diagnostic)
        => GetFixInfo(compilationUnitSyntax, compilationUnitSyntax.FindNode(diagnostic.Location.SourceSpan), diagnostic);
}

/// <summary>
/// Provides a base class for code fix providers for analyzers that report diagnostics on <see cref="IOperation"/>s.
/// </summary>
/// <param name="fixableDiagnosticIds">An <see cref="ImmutableArray{T}"/> of fixable diagnostic IDs.</param>
public abstract class LaquaiLibOperationFixer(params ImmutableArray<string> fixableDiagnosticIds) : LaquaiLibFixer(fixableDiagnosticIds)
{
    /// <summary>
    /// When overridden in a derived class, provides the fix information for a specific <see cref="Diagnostic"/>.
    /// </summary>
    /// <param name="compilationUnitSyntax">The <see cref="CompilationUnitSyntax"/> of the document.</param>
    /// <param name="operation">The <see cref="IOperation"/> on which <paramref name="diagnostic"/> was reported.</param>
    /// <param name="diagnostic">The <see cref="Diagnostic"/> to fix.</param>
    /// <returns>A <see cref="FixInfo"/> containing the fix information.</returns>
    public abstract FixInfo GetFixInfo(CompilationUnitSyntax compilationUnitSyntax, IOperation operation, Diagnostic diagnostic);
    /// <summary>
    /// Do not use. Override <see cref="GetFixInfo(CompilationUnitSyntax, IOperation, Diagnostic)"/> instead.
    /// </summary>
    public sealed override FixInfo GetFixInfo(CompilationUnitSyntax compilationUnitSyntax, Diagnostic diagnostic) => FixInfo.Empty;
    /// <inheritdoc/>
    public sealed override async ValueTask<FixInfo> GetFixInfoAsync(Document document, CompilationUnitSyntax compilationUnitSyntax, Diagnostic diagnostic, CancellationToken cancellationToken)
    {
        var semanticModel = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        if (semanticModel is null)
        {
            return FixInfo.Empty;
        }

        var node = compilationUnitSyntax.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true);
        foreach (var current in node.AncestorsAndSelf())
        {
            if (semanticModel.GetOperation(current, cancellationToken) is { } operation)
            {
                return GetFixInfo(compilationUnitSyntax, operation, diagnostic);
            }
        }

        return FixInfo.Empty;
    }
}

/// <summary>
/// Provides a base class for code fix providers for analyzers that report diagnostics on <see cref="IOperation"/>s.
/// </summary>
/// <typeparam name="TOperation">The concrete <see cref="IOperation"/> type to dispatch to.</typeparam>
/// <param name="fixableDiagnosticIds">An <see cref="ImmutableArray{T}"/> of fixable diagnostic IDs.</param>
public abstract class LaquaiLibOperationFixer<TOperation>(params ImmutableArray<string> fixableDiagnosticIds) : LaquaiLibOperationFixer(fixableDiagnosticIds)
    where TOperation : class, IOperation
{
    /// <summary>
    /// When overridden in a derived class, provides the fix information for a specific <typeparamref name="TOperation"/>.
    /// </summary>
    /// <param name="compilationUnitSyntax">The <see cref="CompilationUnitSyntax"/> of the document.</param>
    /// <param name="operation">The <typeparamref name="TOperation"/> on which <paramref name="diagnostic"/> was reported.</param>
    /// <param name="diagnostic">The <see cref="Diagnostic"/> to fix.</param>
    /// <returns>A <see cref="FixInfo"/> containing the fix information.</returns>
    public abstract FixInfo GetFixInfo(CompilationUnitSyntax compilationUnitSyntax, TOperation operation, Diagnostic diagnostic);
    /// <summary>
    /// Do not use. Override <see cref="GetFixInfo(CompilationUnitSyntax, TOperation, Diagnostic)"/> instead.
    /// </summary>
    public sealed override FixInfo GetFixInfo(CompilationUnitSyntax compilationUnitSyntax, IOperation operation, Diagnostic diagnostic)
        => operation is TOperation typed ? GetFixInfo(compilationUnitSyntax, typed, diagnostic) : FixInfo.Empty;
}

/// <summary>
/// Encapsulates the information required to construct a <see cref="CodeAction"/>.
/// </summary>
public readonly partial struct FixInfo
{
    private static readonly Regex _keyNormalizationRegex = new Regex(@"[^A-Za-z]", RegexOptions.Compiled);

    /// <summary>
    /// Gets a <see cref="FixInfo"/> with no fix information. Executing it does nothing.
    /// </summary>
    public static FixInfo Empty { get; } = new FixInfo();

    /// <summary>
    /// Initializes a new empty <see cref="FixInfo"/>.
    /// </summary>
    public FixInfo() : this("", _ => default, null, false) { }
    /// <summary>
    /// Initializes a new <see cref="FixInfo"/> with the specified code action data.
    /// </summary>
    /// <param name="title">The title of the code action.</param>
    /// <param name="fixAction">The action that applies the fix through a <see cref="DocumentEditor"/>.</param>
    /// <param name="equivalenceKey">The equivalence key for the code action, or <see langword="null"/> to generate one from <paramref name="title"/>.</param>
    public FixInfo(string title, Func<DocumentEditor, ValueTask> fixAction, string equivalenceKey = null) : this(title, fixAction, equivalenceKey, true) { }
    private FixInfo(string title, Func<DocumentEditor, ValueTask> fixAction, string equivalenceKey, bool hasFix)
    {
        Title = title;
        EquivalenceKey = !string.IsNullOrWhiteSpace(equivalenceKey) ? equivalenceKey : _keyNormalizationRegex.Replace(title.ToTitleCase(), "");
        FixAction = fixAction;

        HasFix = hasFix;
    }

    /// <summary>
    /// Deconstructs this <see cref="FixInfo"/> into its title, equivalence key, and fix action.
    /// </summary>
    /// <param name="title">Receives <see cref="Title"/>.</param>
    /// <param name="equivalenceKey">Receives <see cref="EquivalenceKey"/>.</param>
    /// <param name="fixAction">Receives <see cref="FixAction"/>.</param>
    public void Deconstruct(out string title, out string equivalenceKey, out Func<DocumentEditor, ValueTask> fixAction)
    {
        title = Title;
        equivalenceKey = EquivalenceKey;
        fixAction = FixAction;
    }
    /// <summary>
    /// Gets whether this <see cref="FixInfo"/> is not equivalent to <see cref="Empty"/> (that is, whether executing it has any effect on the document).
    /// </summary>
    public readonly bool HasFix { get; }
    /// <summary>
    /// The title of the code action.
    /// </summary>
    public string Title { get; }
    /// <summary>
    /// The equivalence key for the code action. Need not be set, in which case it is automatically generated from <see cref="Title"/>.
    /// </summary>
    public string EquivalenceKey { get; }
    /// <summary>
    /// A <see langword="delegate"/> that uses a <see cref="DocumentEditor"/> to produce a new <see cref="Document"/> that contains the fix.
    /// </summary>
    public Func<DocumentEditor, ValueTask> FixAction { get; }
}
