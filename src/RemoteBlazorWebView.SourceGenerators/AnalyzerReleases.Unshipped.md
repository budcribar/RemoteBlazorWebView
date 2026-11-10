; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
RBW001 | RemoteBlazorWebView | Warning | BlazorWebView member not found
RBW002 | RemoteBlazorWebView | Error | Not a RemoteBlazorWebView
RBW003 | RemoteBlazorWebView | Error | [RemoteBlazor] class must be partial
RBW004 | RemoteBlazorWebView | Error | Remote mode needs a server
RBW005 | RemoteBlazorWebView | Error | ServerUri is not an absolute uri
RBW006 | RemoteBlazorWebView | Error | Id is not a Guid
RBW007 | RemoteBlazorWebView | Warning | Unknown RemoteBlazorMode
RBW008 | RemoteBlazorWebView | Warning | ServerUri is not used in Runtime mode
RBW009 | RemoteBlazorWebView | Error | Mirror and ReverseMirror together
RBW010 | RemoteBlazorWebView | Error | ReverseMirror is not a WebView2
RBW011 | RemoteBlazorWebView | Error | ShareControl with Mirror or ReverseMirror
RBW012 | RemoteBlazorWebView | Warning | Voice is only available with the WPF control
