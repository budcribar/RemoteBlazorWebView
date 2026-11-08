using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit { }
}

namespace PeakSWC.RemoteBlazorWebView.RemoteComponents.Generators
{
    /// <summary>
    /// For an interface marked [RemoteComponentContract], writes the app's frame component (a [Parameter] per get-only property, an
    /// EventCallback per Task method) and the component process's receiver (implements the interface; values from the app, methods send
    /// callbacks). See the RemoteComponents README.
    /// </summary>
    [Generator(LanguageNames.CSharp)]
    public sealed class ContractGenerator : IIncrementalGenerator
    {
        private const string AttributeName = "PeakSWC.RemoteBlazorWebView.RemoteComponents.RemoteComponentContractAttribute";
        private const string Ns = "global::PeakSWC.RemoteBlazorWebView.RemoteComponents";
        private const string Components = "global::Microsoft.AspNetCore.Components";

        private static readonly DiagnosticDescriptor BadMember = new(
            "RBWRC001", "Member can't be in a remote component contract",
            "'{0}' can't be in a remote component contract: use get-only properties (values from the app) and methods that return Task with at most one parameter (callbacks to the app)",
            "RemoteComponents", DiagnosticSeverity.Error, isEnabledByDefault: true);

        private static readonly DiagnosticDescriptor BadContract = new(
            "RBWRC002", "Contract must be a non-generic top-level interface",
            "'{0}' must be a non-generic interface that isn't nested in a type, for [RemoteComponentContract]",
            "RemoteComponents", DiagnosticSeverity.Error, isEnabledByDefault: true);

        private static readonly DiagnosticDescriptor ReservedMember = new(
            "RBWRC003", "Member name is used by the remote component base class",
            "'{0}' is already defined on RemoteComponentFrameBase or RemoteComponentReceiverBase; choose another name for the contract member",
            "RemoteComponents", DiagnosticSeverity.Error, isEnabledByDefault: true);

        private static readonly HashSet<string> ReservedNames = new(StringComparer.Ordinal)
        {
            "Open", "Title", "Sandbox", "MinHeight", "MaxHeight", "InitialHeight", "ProblemContent", "AdditionalAttributes",
            "Session", "Problem", "GetContractValues", "OnCallAsync", "ReadArg", "BuildRenderTree", "OnParametersSet",
            "OnParametersSetAsync", "OnInitialized", "OnInitializedAsync", "OnAfterRender", "OnAfterRenderAsync",
            "SetParametersAsync", "StateHasChanged", "ShouldRender", "InvokeAsync", "DisposeAsync",
            "Connection", "Changed", "HasValues", "BadValue", "Start", "OnMessageAsync", "Apply", "SendCallAsync", "Read",
            "Equals", "GetHashCode", "GetType", "ToString", "MemberwiseClone",
        };

        private static readonly SymbolDisplayFormat TypeFormat = SymbolDisplayFormat.FullyQualifiedFormat
            .AddMiscellaneousOptions(SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

        internal sealed record Value(string Name, string Type, string TypeOf);
        internal sealed record Callback(string Name, string? ArgType, string? ArgName);
        internal sealed record Problem(DiagnosticDescriptor Descriptor, Location? Location, string Arg);

        internal sealed record Contract(
            string? Namespace, string Interface, string Accessibility, string Frame, string Receiver,
            ImmutableArray<Value> Values, ImmutableArray<Callback> Callbacks, ImmutableArray<Problem> Problems)
        {
            // Locations and arrays: compare by content so incremental caching works.
            public bool Equals(Contract? other) => other is not null && Namespace == other.Namespace && Interface == other.Interface &&
                Accessibility == other.Accessibility && Frame == other.Frame && Receiver == other.Receiver &&
                Values.SequenceEqual(other.Values) && Callbacks.SequenceEqual(other.Callbacks) &&
                Problems.Select(p => (p.Descriptor.Id, p.Arg, p.Location?.ToString())).SequenceEqual(other.Problems.Select(p => (p.Descriptor.Id, p.Arg, p.Location?.ToString())));

            public override int GetHashCode() => (Namespace, Interface, Frame, Receiver, Values.Length, Callbacks.Length).GetHashCode();
        }

        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var contracts = context.SyntaxProvider.ForAttributeWithMetadataName(AttributeName,
                static (node, _) => node is InterfaceDeclarationSyntax,
                static (ctx, _) => Read((INamedTypeSymbol)ctx.TargetSymbol, ctx.Attributes[0]));
            context.RegisterSourceOutput(contracts, static (spc, c) => Emit(spc, c));
        }

        internal static Contract Read(INamedTypeSymbol type, AttributeData attribute)
        {
            var problems = ImmutableArray.CreateBuilder<Problem>();
            var location = type.Locations.FirstOrDefault();
            if (type.Arity > 0 || type.ContainingType is not null)
                problems.Add(new Problem(BadContract, location, type.ToDisplayString()));

            var baseName = type.Name.Length > 1 && type.Name[0] == 'I' && char.IsUpper(type.Name[1]) ? type.Name.Substring(1) : type.Name;
            string frame = baseName + "Frame", receiver = baseName + "Receiver";
            foreach (var named in attribute.NamedArguments)
            {
                if (named.Key == "FrameName" && named.Value.Value is string f && f.Length > 0) frame = f;
                if (named.Key == "ReceiverName" && named.Value.Value is string r && r.Length > 0) receiver = r;
            }

            var values = ImmutableArray.CreateBuilder<Value>();
            var callbacks = ImmutableArray.CreateBuilder<Callback>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var member in new[] { type }.Concat(type.AllInterfaces).SelectMany(t => t.GetMembers()))
            {
                if (member.IsStatic) continue;
                if (member is IMethodSymbol { MethodKind: MethodKind.PropertyGet or MethodKind.PropertySet or MethodKind.EventAdd or MethodKind.EventRemove or MethodKind.EventRaise }) continue;
                if (!seen.Add(member.Name)) continue;
                if (ReservedNames.Contains(member.Name))
                {
                    problems.Add(new Problem(ReservedMember, member.Locations.FirstOrDefault() ?? location, member.ToDisplayString()));
                    continue;
                }
                switch (member)
                {
                    case IPropertySymbol p when p.GetMethod is not null && p.SetMethod is null && !p.IsIndexer && p.Type.TypeKind != TypeKind.Pointer:
                        values.Add(new Value(p.Name, p.Type.ToDisplayString(TypeFormat),
                            p.Type.WithNullableAnnotation(NullableAnnotation.NotAnnotated).ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));
                        break;
                    case IMethodSymbol m when m.MethodKind == MethodKind.Ordinary && m.Arity == 0 && m.Parameters.Length <= 1 &&
                                              m.Parameters.All(x => x.RefKind == RefKind.None) &&
                                              m.ReturnType.ToDisplayString() == "System.Threading.Tasks.Task":
                        callbacks.Add(m.Parameters.Length == 0
                            ? new Callback(m.Name, null, null)
                            : new Callback(m.Name, m.Parameters[0].Type.ToDisplayString(TypeFormat), m.Parameters[0].Name));
                        break;
                    default:
                        problems.Add(new Problem(BadMember, member.Locations.FirstOrDefault() ?? location, member.ToDisplayString()));
                        break;
                }
            }

            var accessibility = type.DeclaredAccessibility == Accessibility.Public ? "public" : "internal";
            return new Contract(type.ContainingNamespace.IsGlobalNamespace ? null : type.ContainingNamespace.ToDisplayString(),
                type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), accessibility, frame, receiver,
                values.ToImmutable(), callbacks.ToImmutable(), problems.ToImmutable());
        }

        private static void Emit(SourceProductionContext spc, Contract c)
        {
            foreach (var p in c.Problems) spc.ReportDiagnostic(Diagnostic.Create(p.Descriptor, p.Location, p.Arg));
            if (c.Problems.Length > 0) return;
            var prefix = c.Namespace is null ? "" : c.Namespace + ".";
            spc.AddSource($"{prefix}{c.Frame}.g.cs", SourceText.From(Frame(c), Encoding.UTF8));
            spc.AddSource($"{prefix}{c.Receiver}.g.cs", SourceText.From(Receiver(c), Encoding.UTF8));
        }

        private static StringBuilder Start(Contract c)
        {
            var sb = new StringBuilder();
            sb.AppendLine("// <auto-generated/> by PeakSWC.RemoteBlazorWebView.RemoteComponents.Generators from [RemoteComponentContract]");
            sb.AppendLine("#nullable enable");
            sb.AppendLine("#pragma warning disable CS1591");
            sb.AppendLine();
            if (c.Namespace is not null) sb.AppendLine($"namespace {c.Namespace};").AppendLine();
            return sb;
        }

        private const string GeneratedCode = "[global::System.CodeDom.Compiler.GeneratedCode(\"PeakSWC.RemoteBlazorWebView.RemoteComponents.Generators\", \"1.0\")]";

        internal static string Frame(Contract c)
        {
            var sb = Start(c);
            sb.AppendLine($"/// <summary>The app's side of <see cref=\"{c.Interface}\"/>: frames the component and sends it these values; its callbacks come back as EventCallbacks.</summary>");
            sb.AppendLine(GeneratedCode);
            sb.AppendLine($"{c.Accessibility} sealed partial class {c.Frame} : {Ns}.RemoteComponentFrameBase");
            sb.AppendLine("{");
            foreach (var v in c.Values)
                sb.AppendLine($"    [{Components}.Parameter] public {v.Type} {v.Name} {{ get; set; }} = default!;");
            foreach (var cb in c.Callbacks)
                sb.AppendLine(cb.ArgType is null
                    ? $"    [{Components}.Parameter] public {Components}.EventCallback {cb.Name} {{ get; set; }}"
                    : $"    [{Components}.Parameter] public {Components}.EventCallback<{cb.ArgType}> {cb.Name} {{ get; set; }}");
            sb.AppendLine();
            sb.AppendLine("    protected override global::System.Collections.Generic.IEnumerable<(string Name, object? Value, global::System.Type Type)> GetContractValues()");
            sb.AppendLine("    {");
            foreach (var v in c.Values) sb.AppendLine($"        yield return (\"{v.Name}\", {v.Name}, typeof({v.TypeOf}));");
            if (c.Values.Length == 0) sb.AppendLine("        yield break;");
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine("    protected override global::System.Threading.Tasks.Task OnCallAsync(string name, global::System.Text.Json.JsonElement? arg)");
            sb.AppendLine("    {");
            sb.AppendLine("        switch (name)");
            sb.AppendLine("        {");
            foreach (var cb in c.Callbacks)
                sb.AppendLine(cb.ArgType is null
                    ? $"            case \"{cb.Name}\": return {cb.Name}.InvokeAsync();"
                    : $"            case \"{cb.Name}\": return {cb.Name}.InvokeAsync(ReadArg<{cb.ArgType}>(arg));");
            sb.AppendLine("            default: return global::System.Threading.Tasks.Task.CompletedTask;");
            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine("}");
            return sb.ToString();
        }

        internal static string Receiver(Contract c)
        {
            var sb = Start(c);
            sb.AppendLine($"/// <summary>The component process's side of <see cref=\"{c.Interface}\"/>: the values the app sent; calling a method sends that callback to the app.</summary>");
            sb.AppendLine(GeneratedCode);
            sb.AppendLine($"{c.Accessibility} sealed partial class {c.Receiver} : {Ns}.RemoteComponentReceiverBase, {c.Interface}");
            sb.AppendLine("{");
            sb.AppendLine($"    public {c.Receiver}({Ns}.RemoteComponentConnection connection) : base(connection) {{ }}");
            sb.AppendLine();
            foreach (var v in c.Values)
            {
                sb.AppendLine($"    private {v.Type} _{Camel(v.Name)} = default!;");
                sb.AppendLine($"    public {v.Type} {v.Name} => _{Camel(v.Name)};");
            }
            foreach (var cb in c.Callbacks)
                sb.AppendLine(cb.ArgType is null
                    ? $"    public global::System.Threading.Tasks.Task {cb.Name}() => SendCallAsync(\"{cb.Name}\");"
                    : $"    public global::System.Threading.Tasks.Task {cb.Name}({cb.ArgType} @{cb.ArgName}) => SendCallAsync(\"{cb.Name}\", @{cb.ArgName});");
            sb.AppendLine();
            sb.AppendLine("    protected override bool Apply(string name, global::System.Text.Json.JsonElement value)");
            sb.AppendLine("    {");
            sb.AppendLine("        switch (name)");
            sb.AppendLine("        {");
            foreach (var v in c.Values)
                sb.AppendLine($"            case \"{v.Name}\": _{Camel(v.Name)} = Read<{v.Type}>(value); return true;");
            sb.AppendLine("            default: return false;");
            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine("}");
            return sb.ToString();
        }

        private static string Camel(string name) => char.ToLowerInvariant(name[0]) + name.Substring(1);
    }
}
