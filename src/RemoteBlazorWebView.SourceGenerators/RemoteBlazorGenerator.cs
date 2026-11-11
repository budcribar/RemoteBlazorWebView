using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace PeakSWC.RemoteBlazorWebView.SourceGenerators
{
    /// <summary>
    /// For a class marked [RemoteBlazor(nameof(webView), ...)], generates InitializeRemoteBlazor(): the code-behind the
    /// RemoteBlazorWebViewTutorial's ConvertToRemoteBlazor.md has people write by hand (Id, ServerUri, the ReadyToConnect link) plus
    /// its "Advanced Features" (mirror, restart on refresh, shut down on disconnect), chosen by the attribute and the
    /// RemoteBlazorMode MSBuild property.
    /// </summary>
    [Generator(LanguageNames.CSharp)]
    public sealed class RemoteBlazorGenerator : IIncrementalGenerator
    {
        private const string WpfBlazorWebView = "PeakSWC.RemoteBlazorWebView.Wpf.BlazorWebView";
        private const string WinFormsBlazorWebView = "PeakSWC.RemoteBlazorWebView.WindowsForms.BlazorWebView";

        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            context.RegisterPostInitializationOutput(c => c.AddSource(PostInitializationSources.HintName, SourceText.From(PostInitializationSources.Text, Encoding.UTF8)));

            var targets = context.SyntaxProvider.ForAttributeWithMetadataName(
                PostInitializationSources.AttributeMetadataName,
                static (node, _) => node is ClassDeclarationSyntax,
                static (ctx, ct) => Read(ctx, ct));

            var modeProperty = context.AnalyzerConfigOptionsProvider.Select(static (options, _) =>
                options.GlobalOptions.TryGetValue("build_property.RemoteBlazorMode", out var value) ? value?.Trim() ?? "" : "");

            context.RegisterSourceOutput(targets.Combine(modeProperty), static (spc, pair) => Emit(spc, pair.Left, pair.Right));
        }

        private static Target Read(GeneratorAttributeSyntaxContext ctx, System.Threading.CancellationToken ct)
        {
            var type = (INamedTypeSymbol)ctx.TargetSymbol;
            var attribute = ctx.Attributes[0];
            var syntax = (ClassDeclarationSyntax)ctx.TargetNode;
            var location = syntax.Identifier.GetLocation();
            var diagnostics = new List<DiagnosticInfo>();

            var webView = attribute.ConstructorArguments.Length > 0 ? attribute.ConstructorArguments[0].Value as string ?? "" : "";
            var mode = Mode.Runtime;
            var modeFromAttribute = false;
            string? serverUri = null, id = null, reverseMirror = null;
            bool showLink = true, mirror = false, restartOnRefresh = false, shutdownOnDisconnect = false, shareControl = false, unattended = false, voice = false;
            foreach (var named in attribute.NamedArguments)
            {
                switch (named.Key)
                {
                    case "Mode": mode = (Mode)(int)(named.Value.Value ?? 0); modeFromAttribute = true; break;
                    case "ServerUri": serverUri = named.Value.Value as string; break;
                    case "Id": id = named.Value.Value as string; break;
                    case "ShowLink": showLink = named.Value.Value is true; break;
                    case "Mirror": mirror = named.Value.Value is true; break;
                    case "ReverseMirror": reverseMirror = named.Value.Value as string; break;
                    case "ShareControl": shareControl = named.Value.Value is true; break;
                    case "Unattended": unattended = named.Value.Value is true; break;
                    case "Voice": voice = named.Value.Value is true; break;
                    case "RestartOnRefresh": restartOnRefresh = named.Value.Value is true; break;
                    case "ShutdownOnDisconnect": shutdownOnDisconnect = named.Value.Value is true; break;
                }
            }
            var attributeLocation = attribute.ApplicationSyntaxReference?.GetSyntax(ct).GetLocation() ?? location;

            // The class, and every class it's nested in, must be partial and non-generic.
            var containing = new List<string>();
            var canEmit = true;
            for (var t = type; t is not null; t = t.ContainingType)
            {
                var partial = t.DeclaringSyntaxReferences.Any(r => r.GetSyntax(ct) is ClassDeclarationSyntax c && c.Modifiers.Any(SyntaxKind.PartialKeyword));
                if (!partial || t.Arity > 0 || t.TypeKind != TypeKind.Class)
                {
                    diagnostics.Add(DiagnosticInfo.Create(Diagnostics.NotPartial, location, t.ToDisplayString()));
                    canEmit = false;
                    break;
                }
                if (!SymbolEqualityComparer.Default.Equals(t, type)) containing.Insert(0, t.Name);
            }

            // The BlazorWebView: a field or property of this class (or a base class), e.g. the field XAML makes for x:Name.
            var framework = UiFramework.Unknown;
            var member = FindMember(type, webView);
            if (member is null)
                diagnostics.Add(DiagnosticInfo.Create(Diagnostics.MemberNotFound, attributeLocation, webView, type.Name));
            else
            {
                var memberType = member is IFieldSymbol f ? f.Type : ((IPropertySymbol)member).Type;
                framework = FrameworkOf(memberType);
                if (framework == UiFramework.Unknown)
                {
                    diagnostics.Add(DiagnosticInfo.Create(Diagnostics.NotRemoteBlazorWebView, attributeLocation, webView, memberType.ToDisplayString()));
                    canEmit = false;
                }
            }

            // Reverse mirror: a WebView2 the app drives through the server. Missing (the XAML first pass) is handled like a missing view.
            if (reverseMirror is not null)
            {
                if (mirror)
                {
                    diagnostics.Add(DiagnosticInfo.Create(Diagnostics.MirrorAndReverseMirror, attributeLocation));
                    canEmit = false;
                }
                var wv2 = FindMember(type, reverseMirror);
                if (wv2 is null)
                {
                    diagnostics.Add(DiagnosticInfo.Create(Diagnostics.MemberNotFound, attributeLocation, reverseMirror, type.Name));
                    framework = UiFramework.Unknown;
                }
                else
                {
                    var wv2Type = wv2 is IFieldSymbol f2 ? f2.Type : ((IPropertySymbol)wv2).Type;
                    if (!HasUriSource(wv2Type))
                    {
                        diagnostics.Add(DiagnosticInfo.Create(Diagnostics.NotWebView2, attributeLocation, reverseMirror, wv2Type.ToDisplayString()));
                        canEmit = false;
                    }
                }
            }

            // Shared control already decides what this window and the browser show.
            shareControl |= unattended;
            if (shareControl && (mirror || reverseMirror is not null))
            {
                diagnostics.Add(DiagnosticInfo.Create(Diagnostics.ShareControlAndMirror, attributeLocation));
                canEmit = false;
            }

            if (voice && framework == UiFramework.WinForms)
                diagnostics.Add(DiagnosticInfo.Create(Diagnostics.VoiceNeedsWpf, attributeLocation));

            if (serverUri is not null && !Uri.TryCreate(serverUri, UriKind.Absolute, out _))
            {
                diagnostics.Add(DiagnosticInfo.Create(Diagnostics.BadServerUri, attributeLocation, serverUri));
                canEmit = false;
            }
            if (id is not null && !Guid.TryParse(id, out _))
            {
                diagnostics.Add(DiagnosticInfo.Create(Diagnostics.BadId, attributeLocation, id));
                canEmit = false;
            }

            return new Target(
                type.ContainingNamespace.IsGlobalNamespace ? null : type.ContainingNamespace.ToDisplayString(),
                new EquatableArray<string>(containing.ToImmutableArray()),
                type.Name, webView, framework, mode, modeFromAttribute, serverUri, id,
                showLink, mirror, reverseMirror, shareControl, unattended, voice, restartOnRefresh, shutdownOnDisconnect, canEmit,
                LocationInfo.From(attributeLocation),
                new EquatableArray<DiagnosticInfo>(diagnostics.ToImmutableArray()));
        }

        private static ISymbol? FindMember(INamedTypeSymbol type, string name)
        {
            for (var t = type; t is not null; t = t.BaseType)
                foreach (var m in t.GetMembers(name))
                    if (m is IFieldSymbol or IPropertySymbol { IsIndexer: false }) return m;
            return null;
        }

        private static bool HasUriSource(ITypeSymbol type)
        {
            for (var t = type as INamedTypeSymbol; t is not null; t = t.BaseType)
                foreach (var m in t.GetMembers("Source"))
                    if (m is IPropertySymbol { SetMethod: not null, Type: var pt } && pt.ToDisplayString().TrimEnd('?') == "System.Uri") return true;
            return false;
        }

        private static UiFramework FrameworkOf(ITypeSymbol type)
        {
            for (var t = type as INamedTypeSymbol; t is not null; t = t.BaseType)
            {
                var name = t.ToDisplayString();
                if (name == WpfBlazorWebView) return UiFramework.Wpf;
                if (name == WinFormsBlazorWebView) return UiFramework.WinForms;
            }
            return UiFramework.Unknown;
        }

        internal static Mode ResolveMode(Target target, string property, List<Diagnostic> diagnostics)
        {
            if (property.Length == 0) return target.Mode;
            if (Enum.TryParse<Mode>(property, ignoreCase: true, out var fromProperty) && Enum.IsDefined(typeof(Mode), fromProperty) && !char.IsDigit(property[0]))
                return fromProperty;
            diagnostics.Add(Diagnostic.Create(Diagnostics.BadModeProperty, target.Location?.ToLocation(), property));
            return target.Mode;
        }

        private static void Emit(SourceProductionContext spc, Target target, string modeProperty)
        {
            foreach (var d in target.Diagnostics) spc.ReportDiagnostic(d.ToDiagnostic());
            var diagnostics = new List<Diagnostic>();
            var mode = ResolveMode(target, modeProperty, diagnostics);
            var canEmit = target.CanEmit;
            if (mode == Mode.Remote && target.ServerUri is null && target.Framework != UiFramework.Unknown)
            {
                diagnostics.Add(Diagnostic.Create(Diagnostics.RemoteNeedsServer, target.Location?.ToLocation()));
                canEmit = false;
            }
            if (mode == Mode.Runtime && target.ServerUri is not null)
                diagnostics.Add(Diagnostic.Create(Diagnostics.ServerUriIgnored, target.Location?.ToLocation()));
            foreach (var d in diagnostics) spc.ReportDiagnostic(d);
            if (!canEmit) return;

            var hint = string.Join(".", new[] { target.Namespace ?? "global" }.Concat(target.ContainingTypes).Concat(new[] { target.TypeName })) + ".RemoteBlazor.g.cs";
            spc.AddSource(hint, SourceText.From(Generate(target, mode), Encoding.UTF8));
        }

        internal static string Generate(Target target, Mode mode)
        {
            var sb = new StringBuilder();
            sb.AppendLine("// <auto-generated/> by PeakSWC.RemoteBlazorWebView.SourceGenerators from [RemoteBlazor]");
            sb.AppendLine("#nullable enable");
            sb.AppendLine();
            var indent = "";
            if (target.Namespace is not null)
            {
                sb.AppendLine($"namespace {target.Namespace}");
                sb.AppendLine("{");
                indent = "    ";
            }
            foreach (var outer in target.ContainingTypes)
            {
                sb.AppendLine($"{indent}partial class {outer}");
                sb.AppendLine($"{indent}{{");
                indent += "    ";
            }
            sb.AppendLine($"{indent}partial class {target.TypeName}");
            sb.AppendLine($"{indent}{{");
            var body = indent + "    ";
            foreach (var line in Method(target, mode)) sb.AppendLine(line.Length == 0 ? "" : body + line);
            sb.AppendLine($"{indent}}}");
            while (indent.Length > 0)
            {
                indent = indent.Substring(4);
                sb.AppendLine($"{indent}}}");
            }
            return sb.ToString();
        }

        private static IEnumerable<string> Method(Target t, Mode mode)
        {
            var modeText = mode.ToString();
            yield return "/// <summary>";
            yield return $"/// Sets up {t.WebView} for remote use (generated from [RemoteBlazor], mode {modeText}). Call it after InitializeComponent(),";
            yield return "/// before the view loads. Returns true when the view is remote. Pass a startup to supply values the app has already";
            yield return "/// worked out (server, id, unattended, voice, restart arguments); anything left null comes from the command line.";
            yield return "/// </summary>";
            yield return "[global::System.CodeDom.Compiler.GeneratedCode(\"PeakSWC.RemoteBlazorWebView.SourceGenerators\", \"1.0\")]";
            yield return "private bool InitializeRemoteBlazor(global::PeakSWC.RemoteBlazorWebView.RemoteBlazorStartup? startup = null)";
            yield return "{";
            if (mode == Mode.Local || t.Framework == UiFramework.Unknown)
            {
                yield return t.Framework == UiFramework.Unknown
                    ? $"    // {t.WebView} wasn't found when this was generated (see warning RBW001), so there is nothing to set up."
                    : "    // Local mode: the BlazorWebView runs in this window, like Microsoft's BlazorWebView.";
                yield return "    return false;";
                yield return "}";
                yield break;
            }

            var wpf = t.Framework == UiFramework.Wpf;
            yield return $"    var webView = this.{t.WebView};";
            const string commandLine = "global::PeakSWC.RemoteBlazorWebView.RemoteBlazorCommandLine";
            yield return mode == Mode.Remote
                ? $"    var serverUri = startup?.ServerUri ?? {commandLine}.ServerUri ?? new global::System.Uri({Literal(t.ServerUri!)});"
                : $"    var serverUri = startup?.ServerUri ?? {commandLine}.ServerUri;";
            if (mode == Mode.Runtime)
                yield return "    if (serverUri is null) return false; // no server from the startup or -u=<server uri>: local";
            yield return "";
            yield return "    webView.ServerUri = serverUri;";
            yield return t.Id is null
                ? $"    webView.Id = startup?.Id ?? {commandLine}.Id ?? global::System.Guid.Empty; // Guid.Empty: a new id each run"
                : $"    webView.Id = startup?.Id ?? {commandLine}.Id ?? new global::System.Guid({Literal(t.Id)});";
            if (t.Mirror || t.ReverseMirror is not null || t.ShareControl) yield return "    webView.EnableMirrors = true;";
            if (wpf)
                yield return $"    webView.EnableVoice = startup?.Voice ?? ({(t.Voice ? "true" : "false")} || {commandLine}.Has(\"voice\")); // a voice call needs an https server";

            if (t.ShareControl)
            {
                yield return "";
                yield return "    // Shared control: the app shows in this window with the owner in control; the browser joins at the mirror link and is";
                yield return "    // handed control. Unattended: the remote user is given control as soon as they join.";
                yield return $"    var unattended = startup?.Unattended ?? ({(t.Unattended ? "true" : "false")} || {commandLine}.Has(\"unattended\"));";
                yield return "    // The owner key: only this window's page is the owner (a remote user adding ?role=owner to the mirror link is not).";
                yield return "    webView.OwnerKey = global::System.Convert.ToHexString(global::System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));";
                yield return "    webView.ReadyToConnect += (_, e) => webView.NavigateToUrl(new global::System.Uri($\"{e.Url}app/{e.Id}?role=owner{(unattended ? \"&control=remote\" : \"\")}&key={webView.OwnerKey}\"));";
            }
            else if (t.ReverseMirror is not null)
            {
                yield return "";
                yield return "    // Reverse mirror: this window uses the app through the server; the system browser gets the read-only mirror.";
                yield return $"    webView.ReadyToConnect += (_, e) => {Invoke(wpf, $"this.{t.ReverseMirror}.Source = new global::System.Uri($\"{{e.Url}}app/{{e.Id}}\")")};";
                yield return "    webView.Connected += (_, e) => global::System.Diagnostics.Process.Start(";
                yield return "        new global::System.Diagnostics.ProcessStartInfo { FileName = $\"{e.Url}mirror/{e.Id}\", UseShellExecute = true })?.Dispose();";
            }
            else if (t.ShowLink)
            {
                yield return "";
                yield return "    webView.ReadyToConnect += (sender, e) =>";
                yield return "    {";
                yield return "        var link = global::System.Net.WebUtility.HtmlEncode($\"{e.Url}app/{e.Id}\");";
                yield return "        (sender as global::PeakSWC.RemoteWebView.IBlazorWebView)?.NavigateToString($\"<a href='{link}' target='_blank'>{link}</a>\");";
                yield return "    };";
            }
            if (t.Mirror)
            {
                yield return "";
                yield return $"    webView.Connected += (_, e) => {Invoke(wpf, "webView.WebView?.CoreWebView2?.Navigate($\"{e.Url}mirror/{e.Id}\")")};";
            }
            if (t.RestartOnRefresh)
            {
                yield return "";
                var close = wpf ? "global::System.Windows.Window.GetWindow(webView)?.Close()" : "webView.FindForm()?.Close()";
                yield return "    // Restart on refresh: a fresh copy on the same server and session id, then this one goes away. The id the server";
                yield return "    // assigned is kept here because the view's own properties can only be read on the UI thread.";
                yield return "    var sessionId = webView.Id;";
                yield return "    var restartArguments = startup?.RestartArguments;";
                yield return "    var restarting = 0;";
                yield return "    webView.ReadyToConnect += (_, e) => { if (e.Id != global::System.Guid.Empty) sessionId = e.Id; };";
                yield return "    global::System.Uri restartServer = serverUri;";
                yield return "    global::System.Action startNewCopy = () => global::PeakSWC.RemoteBlazorWebView.RemoteBlazorRestart.StartCopy(";
                yield return $"        restartArguments?.Invoke(restartServer, sessionId) ?? {commandLine}.RestartArguments(restartServer, sessionId));";
                yield return $"    webView.Refreshed += (_, _) => {Invoke(wpf, $"{{ if (global::System.Threading.Interlocked.Exchange(ref restarting, 1) == 0) {{ startNewCopy(); {close}; }} }}")};";
                yield return "    // The window did not respond to the restart request: do it from this background thread and end the process.";
                yield return "    webView.RestartWhenUnresponsive = () =>";
                yield return "    {";
                yield return "        if (global::System.Threading.Interlocked.Exchange(ref restarting, 1) != 0) return;";
                yield return "        startNewCopy();";
                yield return "        global::PeakSWC.RemoteBlazorWebView.RemoteBlazorRestart.ExitProcess();";
                yield return "    };";
            }
            if (t.ShutdownOnDisconnect)
            {
                yield return "";
                var shutdown = wpf ? "global::System.Windows.Application.Current?.Shutdown()" : "global::System.Windows.Forms.Application.Exit()";
                yield return $"    webView.Disconnected += (_, _) => {Invoke(wpf, shutdown)};";
            }
            yield return "    return true;";
            yield return "}";
        }

        /// <summary>The control's events can come from a background thread; run the handler on its UI thread.</summary>
        private static string Invoke(bool wpf, string statement) =>
            wpf ? $"webView.Dispatcher.BeginInvoke(new global::System.Action(() => {statement}))"
                : $"webView.BeginInvoke(new global::System.Action(() => {statement}))";

        private static string Literal(string value) => SymbolDisplay.FormatLiteral(value, quote: true);
    }
}
