extern alias client;

using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using PeakSWC.RemoteWebView;
using Client = client::PeakSWC.RemoteWebView;

namespace RemoteWebView.Lifecycle.Tests;

/// <summary>RemoteWebViewServer.StartAsync: the relay hosted inside the calling process.</summary>
public class EmbeddedServerTests
{
    [Fact]
    public async Task StartAsync_on_port_zero_binds_a_free_loopback_port_over_plain_http()
    {
        await using var server = await RemoteWebViewServer.StartAsync();

        Assert.True(server.Port > 0);
        Assert.Equal("http", server.BaseUri.Scheme);
        Assert.True(IPAddress.IsLoopback(IPAddress.Parse(server.BaseUri.Host)));

        using var http = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false }) { BaseAddress = server.BaseUri };
        var health = await http.GetAsync("health");
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);

        // No redirect to https when serving plain HTTP.
        var contact = await http.GetAsync("");
        Assert.Equal(HttpStatusCode.OK, contact.StatusCode);
        Assert.StartsWith("text/html", contact.Content.Headers.ContentType?.ToString());

        var grpcBase = await http.GetStringAsync("grpcbaseuri");
        Assert.Contains($"http://{server.BaseUri.Authority}/", grpcBase);
    }

    [Fact]
    public async Task Native_grpc_client_reaches_the_server_with_http2_prior_knowledge()
    {
        await using var server = await RemoteWebViewServer.StartAsync();

        // Plain HTTP: browsers and the native client get separate ports (no ALPN to pick HTTP/2 on one port).
        Assert.NotEqual(server.BaseUri, server.GrpcUri);
        Assert.Equal("http", server.GrpcUri.Scheme);

        // Same channel shape RemoteWebView.Client() uses, against an http:// address (h2c).
        using var channel = GrpcChannel.ForAddress(server.GrpcUri, new GrpcChannelOptions { HttpHandler = new SocketsHttpHandler() });
        var ipc = new Client.WebViewIPC.WebViewIPCClient(channel);

        var ids = await ipc.GetIdsAsync(new Google.Protobuf.WellKnownTypes.Empty(), deadline: DateTime.UtcNow.AddSeconds(10));
        Assert.Empty(ids.Responses);
    }

    [Fact]
    public async Task Two_servers_run_side_by_side_and_StopAsync_releases_the_port()
    {
        var a = await RemoteWebViewServer.StartAsync();
        await using var b = await RemoteWebViewServer.StartAsync();
        Assert.NotEqual(a.Port, b.Port);

        int port = a.Port;
        await a.StopAsync();
        await a.DisposeAsync();
        await a.StopAsync(); // idempotent

        using var tcp = new TcpClient();
        await Assert.ThrowsAnyAsync<SocketException>(() => tcp.ConnectAsync(IPAddress.Loopback, port));

        using var http = new HttpClient { BaseAddress = b.BaseUri };
        Assert.Equal(HttpStatusCode.OK, (await http.GetAsync("health")).StatusCode);
    }

    [Fact]
    public async Task WaitForShutdownAsync_completes_after_StopAsync()
    {
        await using var server = await RemoteWebViewServer.StartAsync();
        var wait = server.WaitForShutdownAsync();
        Assert.False(wait.IsCompleted);
        await server.StopAsync();
        await wait.WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task Plain_http_is_refused_off_loopback()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            RemoteWebViewServer.StartAsync(new RemoteWebViewServerOptions { Address = IPAddress.Any }));
    }

    [Fact]
    public async Task Invalid_frame_ancestor_is_refused()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            RemoteWebViewServer.StartAsync(frameAncestors: ["not a uri"]));
    }

    [Fact]
    public async Task MaxClients_rejects_a_new_CreateWebView_but_not_browser_requests()
    {
        await using var server = await RemoteWebViewServer.StartAsync(maxNumClients: 1);

        // One connected client.
        var clients = server.Services.GetRequiredService<ConcurrentDictionary<string, TaskCompletionSource<ServiceState>>>();
        var id = Guid.NewGuid().ToString();
        var tcs = new TaskCompletionSource<ServiceState>();
        tcs.SetResult(new ServiceState(NullLogger<PeakSWC.RemoteWebView.RemoteWebViewService>.Instance, false) { Id = id, Markup = "", Group = "test" });
        clients[id] = tcs;

        using var channel = GrpcChannel.ForAddress(server.GrpcUri);
        var ipc = new Client.WebViewIPC.WebViewIPCClient(channel);
        using var call = ipc.CreateWebView(new Client.CreateWebViewRequest { Id = Guid.NewGuid().ToString(), HtmlHostPath = "wwwroot/index.html", Markup = "", Group = "test" });
        var ex = await Assert.ThrowsAsync<RpcException>(() => call.ResponseStream.MoveNext());
        Assert.Equal(StatusCode.Unavailable, ex.StatusCode);

        // Other traffic is unaffected.
        using var http = new HttpClient { BaseAddress = server.BaseUri };
        Assert.Equal(HttpStatusCode.OK, (await http.GetAsync("health")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await http.GetAsync($"status/{id}")).StatusCode);
    }

    [Fact]
    public async Task FrameAncestors_sets_csp_on_html_and_checks_cross_origin_callers()
    {
        await using var server = await RemoteWebViewServer.StartAsync(frameAncestors: ["'self'", "https://host.example"]);
        using var http = new HttpClient { BaseAddress = server.BaseUri };

        var page = await http.GetAsync("");
        Assert.Equal("frame-ancestors 'self' https://host.example", string.Join(",", page.Headers.GetValues("Content-Security-Policy")));

        async Task<HttpStatusCode> WithOrigin(string origin)
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, "health");
            req.Headers.Add("Origin", origin);
            return (await http.SendAsync(req)).StatusCode;
        }

        Assert.Equal(HttpStatusCode.OK, await WithOrigin($"http://{server.BaseUri.Authority}")); // same origin
        Assert.Equal(HttpStatusCode.OK, await WithOrigin("https://host.example"));
        Assert.Equal(HttpStatusCode.Forbidden, await WithOrigin("https://evil.example"));
    }

    [Fact]
    public async Task Embedded_server_does_not_expose_agent_services()
    {
        await using var server = await RemoteWebViewServer.StartAsync();
        using var http = new HttpClient(new SocketsHttpHandler()) { BaseAddress = server.GrpcUri };
        using var req = new HttpRequestMessage(HttpMethod.Post, "rwvagent.AgentHub/Connect")
        {
            Content = new ByteArrayContent([]),
            Version = HttpVersion.Version20,
            VersionPolicy = HttpVersionPolicy.RequestVersionExact,
        };
        req.Content.Headers.ContentType = new("application/grpc");
        var resp = await http.SendAsync(req);
        await resp.Content.ReadAsByteArrayAsync(); // trailers arrive after the body
        // Unmapped gRPC methods are answered by the gRPC "unimplemented" handler, not the agent hub.
        var status = resp.Headers.TryGetValues("grpc-status", out var v) ? v.First() : resp.TrailingHeaders.TryGetValues("grpc-status", out var t) ? t.First() : null;
        Assert.Equal(((int)StatusCode.Unimplemented).ToString(), status);
    }
}
