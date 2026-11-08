; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
RBWRC001 | RemoteComponents | Error | Member can't be in a remote component contract
RBWRC002 | RemoteComponents | Error | Contract must be a non-generic top-level interface
RBWRC003 | RemoteComponents | Error | Member name is used by the remote component base class
