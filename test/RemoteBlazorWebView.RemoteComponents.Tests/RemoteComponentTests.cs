using Bunit;
using PeakSWC.RemoteBlazorWebView.RemoteComponents;

namespace RemoteBlazorWebView.RemoteComponents.Tests;

public class RemoteComponentTests
{
    private static async Task Eventually(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++) await Task.Delay(10);
        Assert.True(condition());
    }

    [Fact]
    public void Messages_round_trip_and_junk_is_ignored()
    {
        var m = RemoteComponentMessage.Parse(new RemoteComponentMessage { Call = "Picked" }.ToLine())!;
        Assert.Equal("Picked", m.Call);
        Assert.Null(m.Set);
        Assert.DoesNotContain('\n', new RemoteComponentMessage { Call = "x" }.ToLine());
        Assert.Null(RemoteComponentMessage.Parse("not json"));
        Assert.Null(RemoteComponentMessage.Parse(""));
        Assert.Null(RemoteComponentMessage.Parse(null));
    }

    [Theory]
    [InlineData(10, 120)]
    [InlineData(500.2, 501)]
    [InlineData(1e9, 4000)]
    [InlineData(double.NaN, 120)]
    [InlineData(double.PositiveInfinity, 120)]
    public void Heights_are_clamped(double requested, int expected) => Assert.Equal(expected, RemoteComponentFrame.ClampHeight(requested));

    [Fact]
    public void The_sandbox_never_gives_the_frame_the_app_origin()
    {
        Assert.Contains("allow-scripts", RemoteComponentFrame.DefaultSandbox);
        Assert.DoesNotContain("allow-same-origin", RemoteComponentFrame.DefaultSandbox);
        Assert.DoesNotContain("allow-top-navigation", RemoteComponentFrame.DefaultSandbox);
        Assert.DoesNotContain("allow-popups", RemoteComponentFrame.DefaultSandbox);
    }

    [Fact]
    public async Task The_frame_sends_values_then_only_changes_and_raises_callbacks()
    {
        await using var wire = new Wire();
        var receiver = new ClockReceiver(wire.Child);
        var changes = 0;
        receiver.Changed += () => Interlocked.Increment(ref changes);
        receiver.Start();

        await using var ctx = new BunitContext();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        var picked = new System.Collections.Concurrent.ConcurrentQueue<int>();
        var opened = 0;
        var cut = ctx.Render<ClockFrame>(p => p
            .Add(x => x.Open, _ => { Interlocked.Increment(ref opened); return Task.FromResult(new RemoteComponentSession(new Uri("http://127.0.0.1:5000/abc"), wire.App)); })
            .Add(x => x.Zone, "PST")
            .Add(x => x.Count, 1)
            .Add(x => x.Picked, (int v) => picked.Enqueue(v))
            .Add(x => x.Title, "Clock")
            .AddUnmatched("data-testid", "frame"));

        var frame = cut.Find("iframe");
        // No src: the page sets it from data-rbwv-src (a mirror uses data-rbwv-mirror-src), so a mirror never reloads the component.
        Assert.Null(frame.GetAttribute("src"));
        Assert.Equal("http://127.0.0.1:5000/abc", frame.GetAttribute(RemoteComponentFrame.SourceAttribute));
        Assert.Null(frame.GetAttribute(RemoteComponentFrame.MirrorSourceAttribute)); // "abc" isn't a client id
        Assert.Equal(RemoteComponentFrame.DefaultSandbox, frame.GetAttribute("sandbox"));
        Assert.Equal("no-referrer", frame.GetAttribute("referrerpolicy"));
        Assert.Equal("frame", frame.GetAttribute("data-testid"));
        Assert.Contains("height: 360px", frame.GetAttribute("style"));

        await Eventually(() => receiver.Zone == "PST" && receiver.Count == 1);
        Assert.True(receiver.HasValues);

        cut.Render(p => p.Add(x => x.Count, 2)); // Zone unchanged: not sent again
        await Eventually(() => receiver.Count == 2);
        Assert.Equal(2, Volatile.Read(ref changes));
        Assert.Equal(1, Volatile.Read(ref opened));

        var sent = wire.SentToChild.ToArray();
        Assert.Equal(2, sent.Length);
        Assert.NotNull(sent[0].Set);
        Assert.Equal(2, sent[0].Set!.Count);
        Assert.Equal("PST", sent[0].Set["Zone"].GetString());
        Assert.Equal(1, sent[0].Set["Count"].GetInt32());
        Assert.NotNull(sent[1].Set);
        Assert.Single(sent[1].Set!);
        Assert.True(sent[1].Set!.ContainsKey("Count"));
        Assert.False(sent[1].Set!.ContainsKey("Zone"));
        Assert.Equal(2, sent[1].Set["Count"].GetInt32());

        await receiver.Picked(7);
        await Eventually(() => !picked.IsEmpty);
        Assert.Equal([7], picked.ToArray());
    }

    [Fact]
    public async Task A_registered_receiver_is_also_found_as_the_base_and_sees_its_input_end()
    {
        // RemoteComponentHost (ExitWhenInputCloses) starts every receiver this way, so the input is read and its end noticed.
        using var connection = new RemoteComponentConnection(new StringReader(""), null);
        var ended = 0;
        connection.Closed += () => Interlocked.Exchange(ref ended, 1);
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddRemoteComponentReceiver<ClockReceiver>(connection);
        await using var provider = Microsoft.Extensions.DependencyInjection.ServiceCollectionContainerBuilderExtensions.BuildServiceProvider(services);
        var receivers = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetServices<RemoteComponentReceiverBase>(provider).ToList();
        Assert.Same(Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<ClockReceiver>(provider), Assert.Single(receivers));
        await Eventually(() => Volatile.Read(ref ended) == 1);
    }

    [Fact]
    public async Task A_failed_open_shows_the_problem()
    {
        await using var ctx = new BunitContext();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        var cut = ctx.Render<ClockFrame>(p => p.Add(x => x.Open, _ => Task.FromException<RemoteComponentSession>(new InvalidOperationException("no relay"))));
        Assert.Empty(cut.FindAll("iframe"));
        Assert.Contains("no relay", cut.Find("p.remote-component-problem").TextContent);
    }

    [Fact]
    public async Task The_receiver_skips_values_it_cannot_read()
    {
        using var connection = new RemoteComponentConnection(null, null);
        var receiver = new ClockReceiver(connection);
        var bad = new List<string>();
        receiver.BadValue += (n, _) => bad.Add(n);
        var set = new Dictionary<string, System.Text.Json.JsonElement>
        {
            ["Count"] = System.Text.Json.JsonSerializer.SerializeToElement("not a number"),
            ["Zone"] = System.Text.Json.JsonSerializer.SerializeToElement("UTC"),
            ["Unknown"] = System.Text.Json.JsonSerializer.SerializeToElement(1),
        };
        await receiver.OnMessageAsync(new RemoteComponentMessage { Set = set });
        Assert.Equal(["Count"], bad);
        Assert.Equal("UTC", receiver.Zone);
        Assert.Equal(0, receiver.Count);
    }

    [Fact]
    public async Task Disposing_the_frame_stops_the_session()
    {
        var stopped = 0;
        await using var ctx = new BunitContext();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        var cut = ctx.Render<ClockFrame>(p => p.Add(x => x.Open, _ => Task.FromResult(new RemoteComponentSession(new Uri("http://x/1"),
            new RemoteComponentConnection(null, null), () => { Interlocked.Exchange(ref stopped, 1); return ValueTask.CompletedTask; }))));
        await cut.Instance.DisposeAsync();
        Assert.Equal(1, Volatile.Read(ref stopped));
    }

    [Theory]
    [InlineData("http://127.0.0.1:5000/9bfd9d43-0289-4a80-92d8-6e617729da12", "http://127.0.0.1:5000/mirror/9bfd9d43-0289-4a80-92d8-6e617729da12")]
    [InlineData("https://relay.example.com:34544/9bfd9d43-0289-4a80-92d8-6e617729da12/", "https://relay.example.com:34544/mirror/9bfd9d43-0289-4a80-92d8-6e617729da12")]
    [InlineData("https://relay.example.com/base/9bfd9d43-0289-4a80-92d8-6e617729da12?x=1", "https://relay.example.com/base/mirror/9bfd9d43-0289-4a80-92d8-6e617729da12?x=1")]
    [InlineData("http://127.0.0.1:5000/abc", null)]
    [InlineData("http://127.0.0.1:5000/", null)]
    public void The_mirror_source_is_the_relays_mirror_view(string source, string? mirror) =>
        Assert.Equal(mirror, RemoteComponentFrame.MirrorSourceFor(new Uri(source))?.ToString());

    [Fact]
    public async Task The_frame_carries_its_mirror_source()
    {
        var id = Guid.NewGuid();
        await using var ctx = new BunitContext();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        var cut = ctx.Render<ClockFrame>(p => p.Add(x => x.Open, _ => Task.FromResult(new RemoteComponentSession(new Uri($"http://127.0.0.1:5000/{id}"),
            new RemoteComponentConnection(null, null)))));
        var frame = cut.Find("iframe");
        Assert.Null(frame.GetAttribute("src"));
        Assert.Equal($"http://127.0.0.1:5000/{id}", frame.GetAttribute(RemoteComponentFrame.SourceAttribute));
        Assert.Equal($"http://127.0.0.1:5000/mirror/{id}", frame.GetAttribute(RemoteComponentFrame.MirrorSourceAttribute));
    }

    [Fact]
    public async Task A_session_without_a_mirror_source_renders_none()
    {
        await using var ctx = new BunitContext();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        var cut = ctx.Render<ClockFrame>(p => p.Add(x => x.Open, _ => Task.FromResult(new RemoteComponentSession(new Uri($"http://127.0.0.1:5000/{Guid.NewGuid()}"),
            new RemoteComponentConnection(null, null)) { MirrorSource = null })));
        Assert.Null(cut.Find("iframe").GetAttribute(RemoteComponentFrame.MirrorSourceAttribute));
    }
}
