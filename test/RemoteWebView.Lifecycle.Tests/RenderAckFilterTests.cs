extern alias client;

using ClientRemote = client::PeakSWC.RemoteWebView;

namespace RemoteWebView.Lifecycle.Tests;

/// <summary>
/// The host must see each render batch acknowledged exactly once, in order, or Blazor's WebViewRenderer throws on the UI
/// dispatcher. Every browser acknowledges every batch and control can move mid-batch, so acknowledgements can arrive twice or
/// not at all.
/// </summary>
public class RenderAckFilterTests
{
    private static string Batch(long id) => $"__bwv:[\"RenderBatch\",{id},\"AAAA\"]";
    private static string Ack(long id) => $"__bwv:[\"OnRenderCompleted\",{id},null]";

    private static ClientRemote.RenderAckFilter Sent(params long[] batches)
    {
        var filter = new ClientRemote.RenderAckFilter();
        foreach (var b in batches) filter.OnSending(Batch(b));
        return filter;
    }

    [Fact]
    public void An_acknowledgement_in_order_passes_through_unchanged()
    {
        var filter = Sent(1, 2);
        Assert.Equal(new[] { Ack(1) }, filter.OnReceived(Ack(1)));
        Assert.Equal(new[] { Ack(2) }, filter.OnReceived(Ack(2)));
    }

    [Fact]
    public void A_duplicate_acknowledgement_is_dropped()
    {
        var filter = Sent(1);
        Assert.Single(filter.OnReceived(Ack(1)));
        Assert.Empty(filter.OnReceived(Ack(1))); // the old and the new controller both acknowledged batch 1
    }

    [Fact]
    public void A_skipped_acknowledgement_is_filled_in_before_the_next_one()
    {
        var filter = Sent(1, 2, 3);
        Assert.Single(filter.OnReceived(Ack(1)));
        // Batch 2's acknowledgement was lost in a hand-off: the host gets 2 then 3.
        Assert.Equal(new[] { Ack(2), Ack(3) }, filter.OnReceived(Ack(3)));
        Assert.Empty(filter.OnReceived(Ack(2))); // and a late 2 is then a duplicate
    }

    [Fact]
    public void An_acknowledgement_for_a_batch_that_was_never_sent_is_dropped()
    {
        var filter = Sent(1);
        Assert.Empty(filter.OnReceived(Ack(5)));
        Assert.Single(filter.OnReceived(Ack(1))); // and it did not disturb the real one
    }

    [Fact]
    public void An_acknowledgement_before_anything_was_sent_is_dropped()
    {
        Assert.Empty(new ClientRemote.RenderAckFilter().OnReceived(Ack(1)));
    }

    [Theory]
    [InlineData("__bwv:[\"BeginInvokeDotNet\",\"1\",\"asm\",\"Method\",0,\"[]\"]")]
    [InlineData("__bwv:[\"OnLocationChanged\",\"https://0.0.0.0/counter\",null,false]")]
    [InlineData("connected:")]
    [InlineData("")]
    public void Other_messages_pass_through_unchanged(string message)
    {
        var filter = Sent(1);
        Assert.Equal(new[] { message }, filter.OnReceived(message));
    }

    [Fact]
    public void AttachPage_starts_a_new_batch_sequence()
    {
        var filter = Sent(1, 2);
        Assert.Single(filter.OnReceived(Ack(1)));
        Assert.Single(filter.OnReceived(Ack(2)));

        var attach = "__bwv:[\"AttachPage\",\"https://0.0.0.0/\",\"https://0.0.0.0/\"]";
        Assert.Equal(new[] { attach }, filter.OnReceived(attach));

        filter.OnSending(Batch(1)); // the new page context numbers its batches from 1 again
        Assert.Equal(new[] { Ack(1) }, filter.OnReceived(Ack(1)));
    }

    [Fact]
    public void A_restarted_batch_sequence_is_accepted_without_AttachPage()
    {
        var filter = Sent(1, 2, 3);
        filter.OnReceived(Ack(3));
        filter.OnSending(Batch(1)); // batch ids started over
        Assert.Equal(new[] { Ack(1) }, filter.OnReceived(Ack(1)));
    }

    [Fact]
    public void Many_lost_acknowledgements_are_all_filled_in_order()
    {
        var filter = new ClientRemote.RenderAckFilter();
        for (long b = 1; b <= 50; b++) filter.OnSending(Batch(b));
        var forwarded = filter.OnReceived(Ack(50));
        Assert.Equal(50, forwarded.Count);
        for (var i = 0; i < 50; i++) Assert.Equal(Ack(i + 1), forwarded[i]);
    }

    [Fact]
    public async Task Concurrent_acknowledgements_forward_each_batch_exactly_once()
    {
        const int batches = 200;
        var filter = new ClientRemote.RenderAckFilter();
        for (long b = 1; b <= batches; b++) filter.OnSending(Batch(b));

        // Two browsers acknowledge every batch at the same time.
        var seen = new System.Collections.Concurrent.ConcurrentBag<string>();
        await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => Task.Run(() =>
        {
            for (long b = 1; b <= batches; b++)
                foreach (var m in filter.OnReceived(Ack(b))) seen.Add(m);
        })));

        Assert.Equal(batches, seen.Count);
        Assert.Equal(batches, seen.Distinct().Count());
    }
}
