using System.Collections.Immutable;
using Bunit;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using PeakSWC.RemoteBlazorWebView.RemoteComponents;
using PeakSWC.RemoteBlazorWebView.RemoteComponents.Generators;

namespace RemoteBlazorWebView.RemoteComponents.Tests;

public enum GaugeKind { Linear, Round }

public sealed record GaugeReading(DateTimeOffset When, double Value, string? Note);

/// <summary>A contract the generator turns into GaugeFrame (app) and GaugeReceiver (component process).</summary>
[RemoteComponentContract]
public interface IGauge
{
    string Label { get; }
    double? Level { get; }
    GaugeKind Kind { get; }
    IReadOnlyList<GaugeReading>? History { get; }
    Task Reset();
    Task Picked(GaugeReading reading);
}

[RemoteComponentContract(FrameName = "TinyView", ReceiverName = "TinyState")]
internal interface ITiny
{
    int N { get; }
}

public class GeneratedContractTests
{
    private static async Task Eventually(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++) await Task.Delay(10);
        Assert.True(condition());
    }

    [Fact]
    public async Task Generated_frame_and_receiver_carry_values_and_callbacks()
    {
        var wire = new Wire();
        var receiver = new GaugeReceiver(wire.Child);
        IGauge contract = receiver; // the receiver implements the contract
        receiver.Start();

        using var ctx = new BunitContext();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        var resets = 0;
        var picked = new List<GaugeReading>();
        var when = new DateTimeOffset(2026, 11, 6, 12, 0, 0, TimeSpan.FromHours(-8));
        var cut = ctx.Render<GaugeFrame>(p => p
            .Add(x => x.Open, _ => Task.FromResult(new RemoteComponentSession(new Uri("http://127.0.0.1:1/g"), wire.App)))
            .Add(x => x.Label, "CPU")
            .Add(x => x.Level, 0.5)
            .Add(x => x.Kind, GaugeKind.Round)
            .Add(x => x.History, new[] { new GaugeReading(when, 0.4, null) })
            .Add(x => x.Reset, () => resets++)
            .Add(x => x.Picked, (GaugeReading r) => picked.Add(r)));

        await Eventually(() => contract.Label == "CPU" && contract.History is { Count: 1 });
        Assert.Equal(0.5, contract.Level);
        Assert.Equal(GaugeKind.Round, contract.Kind);
        Assert.Equal(when, contract.History![0].When);

        cut.Render(p => p.Add(x => x.Level, (double?)null));
        await Eventually(() => contract.Level is null);

        await contract.Reset();
        await contract.Picked(new GaugeReading(when, 0.9, "peak"));
        await Eventually(() => resets == 1 && picked.Count == 1);
        Assert.Equal("peak", picked[0].Note);
    }

    [Fact]
    public void Names_can_be_chosen_and_internal_contracts_stay_internal()
    {
        Assert.False(typeof(TinyView).IsPublic);
        Assert.True(typeof(RemoteComponentFrameBase).IsAssignableFrom(typeof(TinyView)));
        Assert.True(typeof(ITiny).IsAssignableFrom(typeof(TinyState)));
        Assert.True(typeof(GaugeFrame).IsPublic);
    }

    private static (ImmutableArray<Diagnostic> Diagnostics, int Trees) Generate(string source)
    {
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Select(p => MetadataReference.CreateFromFile(p));
        var compilation = CSharpCompilation.Create("C", [CSharpSyntaxTree.ParseText(source)], references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
        var driver = CSharpGeneratorDriver.Create(new ContractGenerator()).RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);
        var errors = output.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error);
        return (diagnostics.AddRange(errors), driver.GetRunResult().GeneratedTrees.Length);
    }

    [Fact]
    public void Generated_code_compiles_without_errors()
    {
        var (diagnostics, trees) = Generate("""
using PeakSWC.RemoteBlazorWebView.RemoteComponents;
namespace N;
[RemoteComponentContract] public interface IClock { string Zone { get; } System.Threading.Tasks.Task Tick(int n); }
""");
        Assert.Empty(diagnostics);
        Assert.Equal(2, trees);
    }

    [Theory]
    [InlineData("string Zone { get; set; }")]
    [InlineData("int Count();")]
    [InlineData("System.Threading.Tasks.Task Two(int a, int b);")]
    [InlineData("event System.Action Fired;")]
    [InlineData("System.Threading.Tasks.Task<int> Answer();")]
    public void Unsupported_members_are_errors(string member)
    {
        var (diagnostics, trees) = Generate($$"""
using PeakSWC.RemoteBlazorWebView.RemoteComponents;
[RemoteComponentContract] public interface IBad { {{member}} }
""");
        Assert.Contains(diagnostics, d => d.Id == "RBWRC001");
        Assert.Equal(0, trees);
    }

    [Fact]
    public void Generic_and_nested_contracts_are_errors()
    {
        Assert.Contains(Generate("[PeakSWC.RemoteBlazorWebView.RemoteComponents.RemoteComponentContract] public interface IG<T> { T V { get; } }").Diagnostics, d => d.Id == "RBWRC002");
        Assert.Contains(Generate("public class O { [PeakSWC.RemoteBlazorWebView.RemoteComponents.RemoteComponentContract] public interface IN { int V { get; } } }").Diagnostics, d => d.Id == "RBWRC002");
    }

    [Theory]
    [InlineData("string Title { get; }")]
    [InlineData("string Sandbox { get; }")]
    [InlineData("System.Threading.Tasks.Task Open();")]
    [InlineData("string Connection { get; }")]
    public void Reserved_base_member_names_are_errors(string member)
    {
        var (diagnostics, trees) = Generate($$"""
using PeakSWC.RemoteBlazorWebView.RemoteComponents;
[RemoteComponentContract] public interface IReserved { {{member}} }
""");
        Assert.Contains(diagnostics, d => d.Id == "RBWRC003");
        Assert.Equal(0, trees);
    }

    [Fact]
    public void Keyword_parameter_names_in_callbacks_compile_without_errors()
    {
        var (diagnostics, trees) = Generate("""
using PeakSWC.RemoteBlazorWebView.RemoteComponents;
namespace N;
[RemoteComponentContract] public interface IKeywords { System.Threading.Tasks.Task Picked(string @event); }
""");
        Assert.Empty(diagnostics);
        Assert.Equal(2, trees);
    }
}
