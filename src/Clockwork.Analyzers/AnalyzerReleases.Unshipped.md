; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|------------------------------------------------------
CW1001  | Clockwork.Determinism | Info | NondeterministicApiAnalyzer, controlled/rejected BCL, task, thread, thread-pool, Parallel, Monitor, Lock, and SemaphoreSlim surface
CW1002  | Clockwork.Determinism | Warning | NondeterministicApiAnalyzer, complete public static RandomNumberGenerator entropy surface
CW1003  | Clockwork.Determinism | Warning | Position-sensitive Dictionary/HashSet enumeration consumers
