# LaquaiLib.Analyzers.Fixes

Roslyn code fixes for diagnostics produced by `LaquaiLib.Analyzers`.

## What this assembly provides

- `CodeFixProvider` implementations for analyzer diagnostics, organized into Performance, Quality, and Refactorings folders.
- Concrete fixes such as `JoinWithEmptySeparatorAnalyzerFix`, `StringConcatAnalyzerFix`, `RemoveBracesAnalyzerFix`, `AvoidCastAfterCloneAnalyzerFix`, and `RemoveRedundantTryCatchAnalyzerFix`.
- Shared fixer infrastructure in `LaquaiLibFixer`, including node-, token-, and operation-based fixer base classes.
- Helper utilities used to build and apply syntax edits with Roslyn workspace APIs.

## Relationships

- Targets diagnostics defined in [`../LaquaiLib.Analyzers`](../LaquaiLib.Analyzers/README.md).
- Reuses common internal helpers from [`../LaquaiLib.Analyzers.Shared`](../LaquaiLib.Analyzers.Shared/README.md).
