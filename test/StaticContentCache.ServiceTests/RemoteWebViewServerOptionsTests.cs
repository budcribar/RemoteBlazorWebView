using System.Net;
using PeakSWC.RemoteWebView;

namespace StaticContentCache.Tests;

public class RemoteWebViewServerOptionsTests
{
    [Fact]
    public void Validate_rejects_a_port_outside_the_tcp_range()
    {
        var options = new RemoteWebViewServerOptions { Port = 70000 };
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => options.Validate());
        Assert.Equal("port", ex.ParamName);
    }

    [Fact]
    public void Validate_rejects_a_grpc_port_outside_the_tcp_range()
    {
        var options = new RemoteWebViewServerOptions { GrpcPort = -1 };
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => options.Validate());
        Assert.Equal("grpcPort", ex.ParamName);
    }

    [Fact]
    public void Validate_rejects_fewer_than_one_client()
    {
        var options = new RemoteWebViewServerOptions { MaxClients = 0 };
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => options.Validate());
        Assert.Equal("maxClients", ex.ParamName);
    }

    [Fact]
    public void Validate_rejects_a_frame_ancestor_that_is_not_self_or_absolute()
    {
        var options = new RemoteWebViewServerOptions { FrameAncestors = ["not a uri"] };
        var ex = Assert.Throws<InvalidOperationException>(() => options.Validate());
        Assert.Contains("not a uri", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_names_the_first_invalid_frame_ancestor_after_valid_ones()
    {
        var options = new RemoteWebViewServerOptions
        {
            FrameAncestors = ["'self'", "https://app.example.com", "not a uri", "also bad"]
        };
        var ex = Assert.Throws<InvalidOperationException>(() => options.Validate());
        Assert.Equal("Invalid origin: not a uri", ex.Message);
    }

    [Fact]
    public void Validate_accepts_self_and_an_absolute_origin()
    {
        var options = new RemoteWebViewServerOptions { FrameAncestors = ["'self'", "https://app.example.com"] };
        options.Validate();
    }

    [Fact]
    public void Dev_certificate_listen_requires_an_address_and_a_certificate_path()
    {
        Assert.False(RemoteWebViewServer.TryParseDevCertificateListen(null, "cert.pfx", null, out _, out _));
        Assert.False(RemoteWebViewServer.TryParseDevCertificateListen(IPAddress.Loopback.ToString(), " ", null, out _, out _));
        Assert.False(RemoteWebViewServer.TryParseDevCertificateListen("not-an-ip", "cert.pfx", null, out _, out _));
    }

    [Fact]
    public void Dev_certificate_listen_defaults_the_port_and_rejects_an_unusable_one()
    {
        Assert.True(RemoteWebViewServer.TryParseDevCertificateListen(IPAddress.Loopback.ToString(), "cert.pfx", null, out var address, out var port));
        Assert.Equal(IPAddress.Loopback, address);
        Assert.Equal(5002, port);

        Assert.True(RemoteWebViewServer.TryParseDevCertificateListen(IPAddress.Loopback.ToString(), "cert.pfx", "5443", out _, out port));
        Assert.Equal(5443, port);
        Assert.False(RemoteWebViewServer.TryParseDevCertificateListen(IPAddress.Loopback.ToString(), "cert.pfx", "70000", out _, out _));
        Assert.False(RemoteWebViewServer.TryParseDevCertificateListen(IPAddress.Loopback.ToString(), "cert.pfx", "0", out _, out _));
    }
}
