using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.JSInterop;

namespace PeakSWC.RemoteBlazorWebView.RemoteComponents;

/// <summary>What the app's page needs to frame a remote component safely.</summary>
public static class RemoteComponentFrame
{
    /// <summary>No allow-same-origin: the frame gets an opaque origin, so it can't read the app's page, storage or cookies, or another
    /// frame's.</summary>
    public const string DefaultSandbox = "allow-scripts allow-forms";

    /// <summary>The only message a framed component posts to the app's page: <c>{ type: "rbwv-remote-height", height: n }</c>.</summary>
    public const string HeightMessage = "rbwv-remote-height";

    public const int DefaultMinHeight = 120, DefaultMaxHeight = 4000, DefaultInitialHeight = 360;

    /// <summary>The frame height for a height the component asked for: clamped; anything that isn't a finite number gets the minimum.</summary>
    public static int ClampHeight(double requested, int min = DefaultMinHeight, int max = DefaultMaxHeight) =>
        double.IsFinite(requested) ? (int)Math.Clamp(Math.Ceiling(requested), min, max) : min;

    /// <summary>The script a component's host page includes so the app can size the frame (see README.md).</summary>
    public const string HeightScript = """
(function () {
    var last = 0;
    function post() {
        var h = Math.ceil(document.documentElement.scrollHeight);
        if (h !== last) { last = h; window.parent.postMessage({ type: 'rbwv-remote-height', height: h }, '*'); }
    }
    if (window.ResizeObserver) new ResizeObserver(post).observe(document.documentElement);
    window.addEventListener('load', post);
})();
""";
}

/// <summary>
/// The app's side of a remote component: a sandboxed iframe on the component's page, plus the contract values sent to its process
/// whenever they change, and its callbacks raised as EventCallbacks. The <c>[RemoteComponentContract]</c> source generator writes a
/// subclass per contract with a [Parameter] for each value and callback; you rarely derive from this by hand.
/// </summary>
public abstract class RemoteComponentFrameBase : ComponentBase, IAsyncDisposable
{
    private const string ModulePath = "./_content/PeakSWC.RemoteBlazorWebView.RemoteComponents/remotecomponents.js";

    /// <summary>Starts the component's process (or reuses one) and returns its session. Called once, when the frame first renders.</summary>
    [Parameter, EditorRequired] public Func<CancellationToken, Task<RemoteComponentSession>>? Open { get; set; }

    /// <summary>The frame's title, for screen readers.</summary>
    [Parameter] public string? Title { get; set; }

    [Parameter] public string Sandbox { get; set; } = RemoteComponentFrame.DefaultSandbox;
    [Parameter] public int MinHeight { get; set; } = RemoteComponentFrame.DefaultMinHeight;
    [Parameter] public int MaxHeight { get; set; } = RemoteComponentFrame.DefaultMaxHeight;
    [Parameter] public int InitialHeight { get; set; } = RemoteComponentFrame.DefaultInitialHeight;

    /// <summary>Shown instead of the frame when <see cref="Open"/> fails; gets the error message. Default: a plain paragraph.</summary>
    [Parameter] public RenderFragment<string>? ProblemContent { get; set; }

    /// <summary>Other attributes (class, data-testid, ...) go on the iframe.</summary>
    [Parameter(CaptureUnmatchedValues = true)] public IReadOnlyDictionary<string, object>? AdditionalAttributes { get; set; }

    [Inject] private IJSRuntime JS { get; set; } = default!;

    /// <summary>The contract's values as they are now, each with its name and declared type (generated).</summary>
    protected abstract IEnumerable<(string Name, object? Value, Type Type)> GetContractValues();

    /// <summary>The component called the named callback (generated). Unknown names are ignored.</summary>
    protected abstract Task OnCallAsync(string name, JsonElement? arg);

    private readonly CancellationTokenSource _cts = new();
    private readonly SemaphoreSlim _sendGate = new(1, 1);
    private readonly Dictionary<string, string> _sent = new(StringComparer.Ordinal);
    private Task<RemoteComponentSession>? _opening;
    private RemoteComponentSession? _session;
    private string? _problem;
    private int _height = -1;
    private ElementReference _frame;
    private IJSObjectReference? _module;
    private DotNetObjectReference<HeightSink>? _sink;
    private bool _watching;
    private int _disposed;

    /// <summary>The session, once open.</summary>
    protected RemoteComponentSession? Session => _session;

    /// <summary>Why the frame couldn't open, or null.</summary>
    protected string? Problem => _problem;

    protected override async Task OnParametersSetAsync()
    {
        if (_height < 0) _height = Math.Clamp(InitialHeight, MinHeight, MaxHeight);
        if (_problem is not null) return;
        if (_opening is null)
        {
            if (Open is null) { _problem = "no Open function was given"; return; }
            _opening = Open(_cts.Token);
        }
        try
        {
            var session = await _opening;
            if (_session is null)
            {
                _session = session;
                if (_disposed != 0) { await session.DisposeAsync(); return; }
                _ = session.Connection.RunAsync(m => m.Call is { } name ? InvokeAsync(() => OnCallAsync(name, m.Arg)) : Task.CompletedTask, _cts.Token);
            }
            await SendChangesAsync();
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !_cts.IsCancellationRequested)
        {
            _problem = ex.Message;
        }
    }

    /// <summary>Sends the contract values that changed since the last send (all of them the first time).</summary>
    private async Task SendChangesAsync()
    {
        if (_session is not { } session) return;
        await _sendGate.WaitAsync(_cts.Token);
        try
        {
            Dictionary<string, JsonElement>? set = null;
            foreach (var (name, value, type) in GetContractValues())
            {
                var element = JsonSerializer.SerializeToElement(value, type, RemoteComponentJson.Options);
                var text = element.GetRawText();
                if (_sent.TryGetValue(name, out var previous) && previous == text) continue;
                _sent[name] = text;
                (set ??= new(StringComparer.Ordinal))[name] = element;
            }
            if (set is not null) await session.Connection.SendAsync(new RemoteComponentMessage { Set = set }, _cts.Token);
        }
        finally { _sendGate.Release(); }
    }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        if (_problem is not null)
        {
            if (ProblemContent is not null) builder.AddContent(0, ProblemContent(_problem));
            else
            {
                builder.OpenElement(1, "p");
                builder.AddAttribute(2, "class", "remote-component-problem");
                builder.AddContent(3, "This component couldn't start: " + _problem);
                builder.CloseElement();
            }
            return;
        }
        if (_session is null) return;
        builder.OpenElement(10, "iframe");
        builder.AddMultipleAttributes(11, AdditionalAttributes);
        builder.AddAttribute(12, "title", Title);
        builder.AddAttribute(13, "sandbox", Sandbox);
        builder.AddAttribute(14, "referrerpolicy", "no-referrer");
        builder.AddAttribute(15, "src", _session.Source.ToString());
        builder.AddAttribute(16, "style", $"width: 100%; border: 0; display: block; height: {_height}px");
        builder.AddElementReferenceCapture(17, r => _frame = r);
        builder.CloseElement();
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_watching || _session is null || _problem is not null) return;
        _watching = true;
        try
        {
            _module = await JS.InvokeAsync<IJSObjectReference>("import", ModulePath);
            _sink = DotNetObjectReference.Create(new HeightSink(this));
            await _module.InvokeVoidAsync("watch", _frame, _sink);
        }
        catch (JSException) { }
        catch (JSDisconnectedException) { }
        catch (InvalidOperationException) { } // prerendering or no JS runtime
    }

    private void SetHeight(double requested)
    {
        var h = RemoteComponentFrame.ClampHeight(requested, MinHeight, MaxHeight);
        if (h == _height) return;
        _height = h;
        StateHasChanged();
    }

    /// <summary>What the JS listener calls; kept off the component so JS can't invoke anything else on it.</summary>
    public sealed class HeightSink
    {
        private readonly RemoteComponentFrameBase _owner;
        internal HeightSink(RemoteComponentFrameBase owner) => _owner = owner;

        [JSInvokable]
        public Task SetHeight(double height) => _owner.InvokeAsync(() => _owner.SetHeight(height));
    }

    public virtual async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _cts.Cancel();
        if (_module is not null)
        {
            try { await _module.InvokeVoidAsync("unwatch", _frame); } catch { }
            try { await _module.DisposeAsync(); } catch { }
        }
        _sink?.Dispose();
        if (_session is { } s) await s.DisposeAsync();
        GC.SuppressFinalize(this);
    }
}
