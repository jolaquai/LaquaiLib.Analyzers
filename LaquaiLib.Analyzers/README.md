# LaquaiLib.Analyzers

General-purpose Roslyn analyzers for C# projects.

## What this assembly provides

- Diagnostic analyzers grouped by category, including Performance, Quality, Refactorings, Validity, and Reliability.
- Concrete analyzers such as `UseAllocateUninitializedArrayAnalyzer`, `AvoidCastAfterCloneAnalyzer`, `JoinWithEmptySeparatorAnalyzer`, `StringConcatAnalyzer`, `RemoveBracesAnalyzer`, and `RemoveRedundantTryCatchAnalyzer`.
- Shared analyzer infrastructure such as `AnalyzerCategories`, `DiagnosticDescriptorBuilder`, `GlobalAnalyzerOptions`, and local helper utilities.

## Project structure

- `Performance (0XXX)` contains performance-oriented diagnostics.
- `Quality (1XXX)` contains code-quality diagnostics.
- `Refactorings (4XXX)` contains style and simplification diagnostics.
- `Validity (9XXX)` contains correctness-oriented diagnostics.

## Related projects

- Companion fixes live in [`../LaquaiLib.Analyzers.Fixes`](../LaquaiLib.Analyzers.Fixes/README.md).
- Shared internal helpers live in [`../LaquaiLib.Analyzers.Shared`](../LaquaiLib.Analyzers.Shared/README.md).
