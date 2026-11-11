using Microsoft.CodeAnalysis;

namespace PeakSWC.RemoteBlazorWebView.SourceGenerators
{
    internal static class Diagnostics
    {
        private const string Category = "RemoteBlazorWebView";

        public static readonly DiagnosticDescriptor MemberNotFound = new(
            "RBW001", "BlazorWebView member not found",
            "'{1}' has no field or property named '{0}' that [RemoteBlazor] can use; InitializeRemoteBlazor() does nothing",
            Category, DiagnosticSeverity.Warning, isEnabledByDefault: true,
            description: "The XAML compiler's first pass builds the class before the x:Name fields exist, so this is a warning, not an error. In the real build it means the name in [RemoteBlazor(...)] is wrong.");

        public static readonly DiagnosticDescriptor NotRemoteBlazorWebView = new(
            "RBW002", "Not a RemoteBlazorWebView",
            "'{0}' is a {1}, not a PeakSWC.RemoteBlazorWebView BlazorWebView. Reference PeakSWC.RemoteBlazorWebView.Wpf (or .WindowsForms) " +
            "instead of the Microsoft package, and use its namespace in XAML (clr-namespace:PeakSWC.RemoteBlazorWebView.Wpf;assembly=PeakSWC.RemoteBlazorWebView.Wpf).",
            Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor NotPartial = new(
            "RBW003", "[RemoteBlazor] class must be partial",
            "'{0}' must be a non-generic partial class (and so must any class it is nested in) for [RemoteBlazor] to add InitializeRemoteBlazor()",
            Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor RemoteNeedsServer = new(
            "RBW004", "Remote mode needs a server",
            "RemoteBlazorMode is Remote but [RemoteBlazor] sets no ServerUri; set ServerUri = \"https://...\" (a -u= on the command line still overrides it)",
            Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor BadServerUri = new(
            "RBW005", "ServerUri is not an absolute uri",
            "ServerUri \"{0}\" is not an absolute uri",
            Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor BadId = new(
            "RBW006", "Id is not a Guid",
            "Id \"{0}\" is not a Guid",
            Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor BadModeProperty = new(
            "RBW007", "Unknown RemoteBlazorMode",
            "The RemoteBlazorMode MSBuild property is \"{0}\"; use Local, Remote or Runtime. Using the attribute's Mode instead.",
            Category, DiagnosticSeverity.Warning, isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor MirrorAndReverseMirror = new(
            "RBW009", "Mirror and ReverseMirror together",
            "Use Mirror (the mirror shows in this window) or ReverseMirror (this window drives the app, the browser gets the mirror), not both",
            Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor NotWebView2 = new(
            "RBW010", "ReverseMirror is not a WebView2",
            "ReverseMirror '{0}' is a {1}; it must be a WebView2 control (a type with a settable Uri Source property), such as <wv2:WebView2 x:Name=\"{0}\" /> with xmlns:wv2=\"clr-namespace:Microsoft.Web.WebView2.Wpf;assembly=Microsoft.Web.WebView2.Wpf\"",
            Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor ShareControlAndMirror = new(
            "RBW011", "ShareControl with Mirror or ReverseMirror",
            "ShareControl (and Unattended) already puts the app in this window and gives the browser the mirror link; remove Mirror and ReverseMirror",
            Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor VoiceNeedsWpf = new(
            "RBW012", "Voice is only available with the WPF control",
            "Voice = true has no effect here: the voice call is only available with the WPF BlazorWebView",
            Category, DiagnosticSeverity.Warning, isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor ServerUriIgnored = new(
            "RBW008", "ServerUri is not used in Runtime mode",
            "ServerUri is only used in Remote mode; in Runtime mode the view is remote only when the app is started with -u=<server uri>. Set Mode = RemoteBlazorMode.Remote (or <RemoteBlazorMode>Remote</RemoteBlazorMode>) to use it.",
            Category, DiagnosticSeverity.Warning, isEnabledByDefault: true);
    }
}
