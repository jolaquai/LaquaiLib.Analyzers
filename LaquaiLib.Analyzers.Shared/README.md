# LaquaiLib.Analyzers.Shared

Internal shared support library for `LaquaiLib.Analyzers` and `LaquaiLib.Analyzers.Fixes`.

## What this assembly provides

- Common helper and extension code used by the analyzer and fixer assemblies.
- Roslyn-oriented extensions such as `SymbolExtensions` and `TypeExtensions`.
- Additional utility code such as `StringExtensions`, `MethodInfoExtensions`, and assembly-level attributes.

## Intended use

- This project exists to avoid duplicating implementation details across the analyzer and fixer assemblies.
- It is not intended for direct consumption as a standalone package surface.

## Related projects

- Consumed by [`../LaquaiLib.Analyzers`](../LaquaiLib.Analyzers/README.md).
- Consumed by [`../LaquaiLib.Analyzers.Fixes`](../LaquaiLib.Analyzers.Fixes/README.md).
