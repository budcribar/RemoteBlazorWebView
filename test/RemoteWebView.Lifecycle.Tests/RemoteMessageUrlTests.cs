extern alias client;

using ClientRemote = client::PeakSWC.RemoteWebView;

namespace RemoteWebView.Lifecycle.Tests;

public class RemoteMessageUrlTests
{
    private static readonly Guid Id = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Uri Server = new("https://example.com/");
    private static readonly Uri Local = new("https://app.local/");

    [Fact]
    public void Null_server_keeps_the_sender()
    {
        var result = ClientRemote.RemoteMessageUrl.Rewrite("https://example.com/page", null, Id, "index.html", Local);

        Assert.Equal(new Uri("https://example.com/page"), result);
    }

    [Fact]
    public void Unrelated_sender_is_unchanged()
    {
        var result = ClientRemote.RemoteMessageUrl.Rewrite("https://other.example/page", Server, Id, "index.html", Local);

        Assert.Equal(new Uri("https://other.example/page"), result);
    }

    [Fact]
    public void Matching_sender_replaces_the_server_the_id_and_the_host_page()
    {
        var sender = $"https://example.com/{Id}/index.html";

        var result = ClientRemote.RemoteMessageUrl.Rewrite(sender, Server, Id, "index.html", Local);

        Assert.Equal(new Uri("https://app.local/"), result);
    }

    [Fact]
    public void Host_page_text_is_removed_everywhere_it_occurs()
    {
        var sender = $"https://example.com/{Id}/index.html/more/index.html";

        var result = ClientRemote.RemoteMessageUrl.Rewrite(sender, Server, Id, "index.html", Local);

        Assert.Equal(new Uri("https://app.local//more/"), result);
    }

    [Fact]
    public void Null_local_url_uses_an_empty_prefix()
    {
        var result = ClientRemote.RemoteMessageUrl.Rewrite(
            "https://example.com/https://left.example/page",
            Server,
            Id,
            "index.html",
            null);

        Assert.Equal(new Uri("https://left.example/page"), result);
    }

    [Fact]
    public void Non_absolute_remainder_is_rejected()
    {
        Assert.Throws<UriFormatException>(() =>
            ClientRemote.RemoteMessageUrl.Rewrite($"https://example.com/{Id}/keep", Server, Id, "index.html", null));
    }

    [Fact]
    public void Empty_sender_is_not_a_uri()
    {
        Assert.Throws<UriFormatException>(() =>
            ClientRemote.RemoteMessageUrl.Rewrite(null, null, Id, "index.html", Local));
    }
}
