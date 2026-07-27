; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|------------------------------------------------------
CW1001  | Clockwork.Determinism | Info | NondeterministicApiAnalyzer, controlled time/identity/random surface
CW1002  | Clockwork.Determinism | Warning | NondeterministicApiAnalyzer, rejected cryptographic randomness surface
