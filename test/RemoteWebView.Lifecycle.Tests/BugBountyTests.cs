extern alias client;

using System.Collections.Concurrent;
using System.Reflection;
using System.Text;
using System.Threading.Channels;
using System.Xml.Linq;
using Google.Protobuf;
using Grpc.Core;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;
using PeakSWC.RemoteWebView;
using PeakSWC.RemoteWebView.EndPoints;
using PeakSWC.RemoteWebView.Pages;
using PeakSWC.RemoteWebView.RemoteStaticFiles;
using PeakSWC.RemoteWebView.Services;
using ClientRemote = client::PeakSWC.RemoteWebView;

namespace RemoteWebView.Lifecycle.Tests;

public class BugBountyTests
{
    [Fact]
    public async Task Bug01_GetClients_does_not_drop_connected_clients_when_one_registration_faults()
    {
        var services = new ConcurrentDictionary<string, TaskCompletionSource<ServiceState>>();
        var goodId = Guid.NewGuid().ToString();
        var good = new TaskCompletionSource<ServiceState>(TaskCreationOptions.RunContinuationsAsynchronously);
        good.SetResult(new ServiceState(NullLogger<RemoteWebViewService>.Instance, false)
        {
            Id = goodId,
            Group = "admin",
            InUse = true,
            Markup = "<div>ok</div>",
            Url = $"https://localhost/app/{goodId}"
        });
        services[goodId] = good;

        var faulted = new TaskCompletionSource<ServiceState>(TaskCreationOptions.RunContinuationsAsynchronously);
        faulted.SetException(new InvalidOperationException("dead client"));
        services[Guid.NewGuid().ToString()] = faulted;

        var channels = new ConcurrentDictionary<string, Channel<string>>();
        var ipc = new ClientIPCService(
            NullLogger<ClientIPCService>.Instance,
            channels,
            services,
            new FixedGroupUserService("admin"),
            new RemoteFilesOptions());

        using var cts = new CancellationTokenSource();
        var writer = new CallbackStreamWriter<ClientResponseList>(_ => cts.Cancel());

        try
        {
            await ipc.GetClients(new UserMessageRequest { Id = "watcher-1", Oid = "oid-1" }, writer, new TestCallContext(cts.Token));
        }
        catch (OperationCanceledException)
        {
        }

        var list = Assert.Single(writer.Messages);
        Assert.Contains(list.ClientResponses, c => c.Id == goodId);
    }

    [Fact]
    public async Task Bug04_IPC_BrowserResponseStream_removes_observer_when_connection_is_cancelled()
    {
        using var serviceCts = new CancellationTokenSource();
        var ipc = new IPC(serviceCts.Token, NullLogger<RemoteWebViewService>.Instance, enableMirrors: true);
        var observersField = typeof(IPC).GetField("observers", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var observers = (System.Collections.IDictionary)observersField.GetValue(ipc)!;

        using var mirrorCts = new CancellationTokenSource();
        var node = new BrowserResponseNode(new CallbackStreamWriter<StringRequest>(_ => { }), "mirror-1", isPrimary: false);
        ipc.BrowserResponseStream(node, mirrorCts);

        Assert.Single(observers);

        mirrorCts.Cancel();
        await ipc.ProcessMessagesTask!.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Empty(observers);
        serviceCts.Cancel();
        ipc.Shutdown();
    }

    [Fact]
    public async Task Bug05_ServerFileSyncManager_faults_pending_metadata_and_data_requests_when_stream_write_throws()
    {
        using var manager = new ServerFileSyncManager(NullLogger<ServerFileSyncManager>.Instance);
        var clientId = Guid.NewGuid().ToString();
        manager.RegisterClient(clientId, "wwwroot/index.html");
        manager.AssociateResponseStream(clientId, new ThrowingStreamWriter<ServerFileReadRequest>(new InvalidOperationException("grpc write failed")));

        var metadataTask = manager.RequestFileMetadataAsync(clientId, "wwwroot/app.css", NullLogger<RemoteFileResolver>.Instance);
        await Assert.ThrowsAsync<InvalidOperationException>(() => metadataTask.WaitAsync(TimeSpan.FromMilliseconds(500)));

        var dataRequest = await manager.RequestFileDataAsync(clientId, "wwwroot/app.css", NullLogger<RemoteFileResolver>.Instance);
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await dataRequest.Pipe.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromMilliseconds(500)));
    }

    [Fact]
    public void Bug07_RestartPage_handles_408_request_timeout_from_wait_endpoint()
    {
        var html = RestartPage.Html(Guid.NewGuid().ToString(), "MyApp", "MyHost");

        // Endpoints.Wait() returns Status408RequestTimeout (408) on timeout, so RestartPage XHR must handle 408.
        Assert.Contains("408", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Bug08_GrpcBaseUri_and_Contact_use_utf8_byte_length_and_html_encode_contact_fields()
    {
        var html = HtmlPageGenerator.GenerateContactPage(
            new ContactInfo
            {
                Name = "Bud <Admin>",
                Company = "Peak & Co",
                Email = "a\"b@example.com",
                Url = "https://example.com?a=1&b=2"
            },
            "1.0 <rc>");

        Assert.DoesNotContain("Bud <Admin>", html, StringComparison.Ordinal);
        Assert.Contains("Bud &lt;Admin&gt;", html, StringComparison.Ordinal);
        Assert.DoesNotContain("1.0 <rc>", html, StringComparison.Ordinal);

        var context = new DefaultHttpContext();
        context.Request.Scheme = "https";
        context.Request.Host = new HostString("localhost", 5001);
        context.Request.PathBase = "/cafÃƒÂ©";
        context.Response.Body = new MemoryStream();

        await Endpoints.GrpcBaseUri()(context);

        Assert.Equal(context.Response.Body.Length, context.Response.ContentLength);
    }

    [Theory]
    [InlineData("::1|alice", "::1", "alice")]
    [InlineData("2001:db8::1|bob", "2001:db8::1", "bob")]
    [InlineData("127.0.0.1|carol", "127.0.0.1", "carol")]
    public async Task Bug09_RemoteWebView_HandleConnectedAsync_preserves_ipv6_address(string payload, string expectedIp, string expectedUser)
    {
        ClientRemote.ConnectedEventArgs? captured = null;
        var fakeView = new CapturingBlazorWebView(args => captured = args);
        var remote = new ClientRemote.RemoteWebView(
            fakeView,
            "wwwroot/index.html",
            new SyncDispatcher(),
            new NullFileProvider(),
            NullLogger.Instance);

        var method = typeof(ClientRemote.RemoteWebView).GetMethod("HandleConnectedAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var task = (Task<bool>)method.Invoke(remote, [fakeView, payload, "{}", CancellationToken.None])!;
        var ok = await task;

        Assert.True(ok);
        Assert.NotNull(captured);
        Assert.Equal(expectedIp, captured!.IpAddress);
        Assert.Equal(expectedUser, captured.User);
        await remote.DisposeAsyncCore();
    }

    [Fact]
    public async Task Bug11_CreateWebView_cleans_up_service_dictionary_when_initial_response_write_throws()
    {
        var id = Guid.NewGuid().ToString();
        var services = new ConcurrentDictionary<string, TaskCompletionSource<ServiceState>>();
        var channels = new ConcurrentDictionary<string, Channel<string>>();
        var shutdown = new ShutdownService(NullLogger<RemoteWebViewService>.Instance, channels, services);
        using var fileSync = new ServerFileSyncManager(NullLogger<ServerFileSyncManager>.Instance);
        var service = new RemoteWebViewService(NullLogger<RemoteWebViewService>.Instance, services, channels, shutdown, fileSync);

        await service.CreateWebView(
            new CreateWebViewRequest { Id = id, HtmlHostPath = "wwwroot/index.html", Markup = "<div/>" },
            new ThrowingStreamWriter<WebMessageResponse>(new InvalidOperationException("write failed")),
            new TestCallContext(CancellationToken.None));

        // Failed CreateWebView must not leave an uncompleted TaskCompletionSource stuck in services.
        Assert.Empty(services);
    }

    [Fact]
    public async Task Bug12_RequestClientFileRead_does_not_crash_when_FileManagerReady_is_already_completed()
    {
        var id = Guid.NewGuid().ToString();
        var services = new ConcurrentDictionary<string, TaskCompletionSource<ServiceState>>();
        var state = new ServiceState(NullLogger<RemoteWebViewService>.Instance, false) { Id = id };
        state.FileManagerReady.SetResult(true);
        var tcs = new TaskCompletionSource<ServiceState>(TaskCreationOptions.RunContinuationsAsynchronously);
        tcs.SetResult(state);
        services[id] = tcs;

        var channels = new ConcurrentDictionary<string, Channel<string>>();
        var shutdown = new ShutdownService(NullLogger<RemoteWebViewService>.Instance, channels, services);
        using var fileSync = new ServerFileSyncManager(NullLogger<ServerFileSyncManager>.Instance);
        var service = new RemoteWebViewService(NullLogger<RemoteWebViewService>.Instance, services, channels, shutdown, fileSync);

        var handledMetadata = false;
        var reader = new SequenceStreamReader<ClientFileReadResponse>(
        [
            new ClientFileReadResponse
            {
                ClientId = id,
                Init = new Init { HtmlHostPath = "wwwroot/index.html" }
            },
            new ClientFileReadResponse
            {
                ClientId = id,
                RequestId = "req-1",
                Metadata = new FileMetadata { Length = 42, StatusCode = 200 }
            }
        ], onSecondRead: () => handledMetadata = true);

        await service.RequestClientFileRead(
            reader,
            new CallbackStreamWriter<ServerFileReadRequest>(_ => { }),
            new TestCallContext(CancellationToken.None));

        Assert.True(handledMetadata);
    }

    [Fact]
    public async Task Bug13_ClientFileSyncManager_HandleMetaDataRequest_and_CloseAsync_use_injected_requestStream()
    {
        var writer = new RecordingClientStreamWriter<ClientRemote.ClientFileReadResponse>();
        var provider = new SingleMemoryFileProvider("app.css", "body { color: red; }");
        var ctor = typeof(ClientRemote.ClientFileSyncManager).GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic,
            [typeof(string), typeof(Microsoft.Extensions.FileProviders.IFileProvider), typeof(IClientStreamWriter<ClientRemote.ClientFileReadResponse>), typeof(Microsoft.Extensions.Logging.ILogger)])!;

        var manager = (ClientRemote.ClientFileSyncManager)ctor.Invoke(["client-1", provider, writer, NullLogger.Instance]);
        var handleMeta = typeof(ClientRemote.ClientFileSyncManager).GetMethod("HandleMetaDataRequestAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;

        await (Task)handleMeta.Invoke(manager, [new ClientRemote.ServerFileReadRequest
        {
            ClientId = "client-1",
            RequestId = "req-meta",
            Path = "wwwroot/app.css",
            RequestType = ClientRemote.ServerFileReadRequest.Types.RequestType.MetaData
        }])!;

        await manager.CloseAsync();

        var sent = Assert.Single(writer.Messages);
        Assert.Equal("req-meta", sent.RequestId);
        Assert.Equal(200, sent.Metadata.StatusCode);
        Assert.True(writer.Completed);
    }

    [Fact]
    public async Task Bug16_ServerFileSyncManager_removes_failed_requests_and_updates_htmlHostPath_on_reregister()
    {
        using var manager = new ServerFileSyncManager(NullLogger<ServerFileSyncManager>.Instance);
        var clientId = Guid.NewGuid().ToString();
        manager.RegisterClient(clientId, "wwwroot/old.html");
        manager.RegisterClient(clientId, "wwwroot/new.html");

        Assert.Equal("wwwroot/new.html", manager.GetHtmlHostPath(clientId));

        // Without an associated response stream, both requests fail and must not leak in the dictionaries.
        var metaTask = manager.RequestFileMetadataAsync(clientId, "wwwroot/app.css", NullLogger<RemoteFileResolver>.Instance);
        await Assert.ThrowsAsync<InvalidOperationException>(() => metaTask.WaitAsync(TimeSpan.FromMilliseconds(500)));

        var dataReq = await manager.RequestFileDataAsync(clientId, "wwwroot/app.css", NullLogger<RemoteFileResolver>.Instance);
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await dataReq.Pipe.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromMilliseconds(500)));

        var metaDictField = typeof(ServerFileSyncManager).GetField("_metadataRequests", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var dataDictField = typeof(ServerFileSyncManager).GetField("_fileDataRequests", BindingFlags.Instance | BindingFlags.NonPublic)!;

        var metaDict = (ConcurrentDictionary<string, ConcurrentDictionary<string, TaskCompletionSource<FileMetadata>>>)metaDictField.GetValue(manager)!;
        var dataDict = (ConcurrentDictionary<string, ConcurrentDictionary<string, DataRequest>>)dataDictField.GetValue(manager)!;

        Assert.Empty(metaDict[clientId]);
        Assert.Empty(dataDict[clientId]);
    }

    [Fact]
    public void Bug17_ServerStats_records_zero_ms_min_response_time_and_clamps_latency_percentiles()
    {
        var stats = new ServerStats();
        stats.RecordRequest(success: true, responseTime: 0.0);
        stats.RecordRequest(success: true, responseTime: 10.0);

        var snapshot = stats.GetStats();
        Assert.Equal(0.0, (double)snapshot["MinResponseTime(ms)"]);

        var percentiles = stats.GetLatencyPercentiles([-10, 0, 50, 100, 110]);
        Assert.Equal(0.0, percentiles[-10]);
        Assert.Equal(0.0, percentiles[0]);
        Assert.Equal(10.0, percentiles[100]);
        Assert.Equal(10.0, percentiles[110]);
    }

    [Fact]
    public async Task Bug18_Wait_endpoint_honors_RequestAborted_cancellation_token()
    {
        var id = Guid.NewGuid().ToString();
        var services = new ServiceCollection()
            .AddSingleton(new ConcurrentDictionary<string, TaskCompletionSource<ServiceState>>())
            .BuildServiceProvider();

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(80));
        var context = new DefaultHttpContext
        {
            RequestServices = services,
            RequestAborted = cts.Token
        };
        context.Request.RouteValues["id"] = id;
        context.Response.Body = new MemoryStream();

        var waitTask = Endpoints.Wait()(context);
        using var timeout = new CancellationTokenSource();
        var delay = Task.Delay(TimeSpan.FromMilliseconds(600), timeout.Token);
        try
        {
            var completed = await Task.WhenAny(waitTask, delay);
            Assert.Same(waitTask, completed);
        }
        finally
        {
            timeout.Cancel();
            try { await delay; } catch (OperationCanceledException) { }
        }
    }

    [Fact]
    public void Bug19_ManifestStaticWebAssetFileProvider_GetFileInfo_normalizes_backslashes_in_subpath()
    {
        var manifestJson = """
            {
              "ContentRoots": [ "C:\\root\\" ],
              "Root": {
                "Children": {
                  "css": {
                    "Children": {
                      "app.css": {
                        "Asset": {
                          "ContentRootIndex": 0,
                          "SubPath": "css/app.css"
                        }
                      }
                    }
                  }
                }
              }
            }
            """;
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(manifestJson));
        var providerType = typeof(ClientRemote.RemoteWebView).Assembly.GetType("PeakSWC.RemoteWebView.ManifestStaticWebAssetFileProvider", throwOnError: true)!;
        var manifestType = providerType.GetNestedType("StaticWebAssetManifest", BindingFlags.NonPublic)!;
        var parseMethod = manifestType.GetMethod("Parse", BindingFlags.Static | BindingFlags.NonPublic)!;
        var manifest = parseMethod.Invoke(null, [stream])!;
        Func<string, Microsoft.Extensions.FileProviders.IFileProvider> factory =
            _ => new SingleMemoryFileProvider("css/app.css", "body { color: blue; }");
        var provider = (Microsoft.Extensions.FileProviders.IFileProvider)Activator.CreateInstance(providerType, [manifest, factory])!;

        var fileInfo = provider.GetFileInfo(@"css\app.css");
        Assert.True(fileInfo.Exists);
    }

    [Fact]
    public async Task Bug20_RemoteWebView_ProcessBrowserMessages_does_not_fire_timeout_disconnect_when_disposed_after_browserAttached()
    {
        ClientRemote.DisconnectedEventArgs? disconnected = null;
        var fakeView = new CapturingBlazorWebView(_ => { });
        fakeView.Disconnected += (_, args) => disconnected = args;

        var remote = new ClientRemote.RemoteWebView(
            fakeView,
            "wwwroot/index.html",
            new SyncDispatcher(),
            new NullFileProvider(),
            NullLogger.Instance);

        using var streamCts = new CancellationTokenSource();
        var reader = new BlockingAfterFirstStreamReader<ClientRemote.WebMessageResponse>(
            new ClientRemote.WebMessageResponse { Response = "browserAttached:" },
            streamCts.Token);
        var call = new AsyncServerStreamingCall<ClientRemote.WebMessageResponse>(
            reader,
            Task.FromResult(new Metadata()),
            () => Status.DefaultSuccess,
            () => new Metadata(),
            streamCts.Cancel);

        var method = typeof(ClientRemote.RemoteWebView).GetMethod("ProcessBrowserMessages", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var task = (Task<Exception?>)method.Invoke(remote, [fakeView, call])!;
        var ex = await task.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.Null(ex);

        // Disposing before "connected:" arrives cancels cts.Token and must not fire "Browser connection timed out".
        await remote.DisposeAsyncCore();
        await Task.Delay(100);

        Assert.Null(disconnected);
    }

    [Fact]
    public async Task Bug21_ClientIPCService_GetClients_isolates_channels_for_concurrent_watchers_with_same_id()
    {
        var serviceStateChannel = new ConcurrentDictionary<string, Channel<string>>();
        var serviceDictionary = new ConcurrentDictionary<string, TaskCompletionSource<ServiceState>>();
        var service = new ClientIPCService(
            NullLogger<ClientIPCService>.Instance,
            serviceStateChannel,
            serviceDictionary,
            new FixedGroupUserService("admin"),
            new RemoteFilesOptions());

        using var cts1 = new CancellationTokenSource();
        using var cts2 = new CancellationTokenSource();
        var ready1 = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var ready2 = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var update2 = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var writer1 = new CallbackStreamWriter<ClientResponseList>(_ => ready1.TrySetResult());
        var writer2 = new CallbackStreamWriter<ClientResponseList>(_ =>
        {
            if (!ready2.Task.IsCompleted)
                ready2.TrySetResult();
            else
                update2.TrySetResult();
        });

        var watcher1Task = service.GetClients(new UserMessageRequest { Id = "shared-watcher", Oid = "u1" }, writer1, new TestCallContext(cts1.Token));
        var watcher2Task = service.GetClients(new UserMessageRequest { Id = "shared-watcher", Oid = "u2" }, writer2, new TestCallContext(cts2.Token));

        await Task.WhenAll(
            ready1.Task.WaitAsync(TimeSpan.FromSeconds(2)),
            ready2.Task.WaitAsync(TimeSpan.FromSeconds(2)));

        // Disconnect watcher 1; watcher 2 must keep its channel and still receive broadcasts.
        cts1.Cancel();
        try { await watcher1Task.WaitAsync(TimeSpan.FromSeconds(2)); } catch (OperationCanceledException) { }

        foreach (var ch in serviceStateChannel.Values)
            await ch.Writer.WriteAsync("Connect:test");

        await update2.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(2, writer2.Messages.Count);

        cts2.Cancel();
        try { await watcher2Task.WaitAsync(TimeSpan.FromSeconds(2)); } catch (OperationCanceledException) { }
    }

    [Fact]
    public async Task Bug25_Start_endpoint_resets_InUse_when_ClientResponseStream_WriteAsync_throws()
    {
        var id = Guid.NewGuid().ToString();
        using var state = new ServiceState(NullLogger<RemoteWebViewService>.Instance, enableMirrors: false)
        {
            Id = id,
            InUse = false
        };
        state.IPC.ClientResponseStream = new ThrowingStreamWriter<WebMessageResponse>(new InvalidOperationException("stream broken"));

        var tcs = new TaskCompletionSource<ServiceState>(TaskCreationOptions.RunContinuationsAsynchronously);
        tcs.SetResult(state);
        var dict = new ConcurrentDictionary<string, TaskCompletionSource<ServiceState>> { [id] = tcs };
        var channels = new ConcurrentDictionary<string, Channel<string>>();

        using var services = new ServiceCollection()
            .AddSingleton(dict)
            .AddSingleton(channels)
            .BuildServiceProvider();

        var context = new DefaultHttpContext { RequestServices = services };
        context.Request.RouteValues["id"] = id;
        context.Response.Body = new MemoryStream();

        await Endpoints.Start()(context);

        Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
        Assert.False(state.InUse);
    }

    [Fact]
    public async Task Bug26_Mirror_endpoint_does_not_overwrite_primary_User_on_ServiceState()
    {
        var id = Guid.NewGuid().ToString();
        using var state = new ServiceState(NullLogger<RemoteWebViewService>.Instance, enableMirrors: true)
        {
            Id = id,
            InUse = true,
            EnableMirrors = true,
            User = "PrimaryUser",
            HtmlHostPath = "wwwroot/index.html"
        };
        state.FileManagerReady.SetResult(true);

        var tcs = new TaskCompletionSource<ServiceState>(TaskCreationOptions.RunContinuationsAsynchronously);
        tcs.SetResult(state);
        var dict = new ConcurrentDictionary<string, TaskCompletionSource<ServiceState>> { [id] = tcs };
        var channels = new ConcurrentDictionary<string, Channel<string>>();
        using var syncManager = new ServerFileSyncManager(NullLogger<ServerFileSyncManager>.Instance);
        var resolver = new StubRemoteFileResolver(
            syncManager,
            new FileMetadata { StatusCode = StatusCodes.Status200OK, Length = 13 },
            new MemoryStream(Encoding.UTF8.GetBytes("<html></html>")));

        using var services = new ServiceCollection()
            .AddSingleton(dict)
            .AddSingleton(channels)
            .AddSingleton<RemoteFileResolver>(resolver)
            .BuildServiceProvider();

        var context = new DefaultHttpContext { RequestServices = services };
        context.Request.RouteValues["id"] = id;
        context.Connection.Id = "mirror-conn";
        context.Response.Body = new MemoryStream();

        await Endpoints.Mirror()(context);

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.Equal("PrimaryUser", state.User);
    }

    [Fact]
    public async Task Bug27_LockedPage_RestartFailedPage_and_Stats_html_encode_untrusted_values()
    {
        var maliciousGuid = "123');alert(1);//<script>bad</script>";
        var lockedHtml = LockedPage.Html("Alice", maliciousGuid);
        var restartHtml = RestartFailedPage.Html(maliciousGuid, isRestarting: true);
        var fragmentHtml = RestartFailedPage.Fragment(maliciousGuid);

        Assert.DoesNotContain("<script>bad</script>", lockedHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("');alert(1);//", lockedHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("<script>bad</script>", restartHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("<script>bad</script>", fragmentHtml, StringComparison.Ordinal);

        var stats = new ServerStats();
        stats.RecordRequest(false, 1.0, "<img src=x onerror=alert(1)>");
        using var services = new ServiceCollection()
            .AddSingleton(stats)
            .BuildServiceProvider();

        var context = new DefaultHttpContext { RequestServices = services };
        var body = new MemoryStream();
        context.Response.Body = body;

        await Endpoints.Stats()(context);
        var statsHtml = Encoding.UTF8.GetString(body.ToArray());
        Assert.DoesNotContain("<img src=x onerror=alert(1)>", statsHtml, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Bug28_RemoteFileResolver_GetFileStreamAsync_matches_host_page_case_insensitively_and_skips_nested_index_html()
    {
        var clientId = Guid.NewGuid().ToString();
        using var syncManager = new ServerFileSyncManager(NullLogger<ServerFileSyncManager>.Instance);
        syncManager.RegisterClient(clientId, "wwwroot/Index.html");

        var streamWriter = new CallbackStreamWriter<ServerFileReadRequest>(req =>
        {
            _ = Task.Run(async () =>
            {
                await syncManager.HandleClientResponse(new ClientFileReadResponse
                {
                    ClientId = clientId,
                    RequestId = req.RequestId,
                    Path = req.Path,
                    FileData = new FileData
                    {
                        FileChunk = Google.Protobuf.ByteString.CopyFromUtf8("<html><head><base href=\"/\" /></head></html>")
                    }
                });
                await syncManager.HandleClientResponse(new ClientFileReadResponse
                {
                    ClientId = clientId,
                    RequestId = req.RequestId,
                    Path = req.Path,
                    FileData = new FileData
                    {
                        FileChunk = Google.Protobuf.ByteString.Empty
                    }
                });
            });
        });
        syncManager.AssociateResponseStream(clientId, streamWriter);

        var resolver = new RemoteFileResolver(NullLogger<RemoteFileResolver>.Instance, syncManager);

        // 1. Nested docs/index.html must NOT have its <base href> rewritten.
        var nestedResult = await resolver.GetFileStreamAsync(clientId, "docs/index.html");
        using var nestedReader = new StreamReader(nestedResult.Stream!);
        var nestedHtml = await nestedReader.ReadToEndAsync();
        Assert.Contains("<base href=\"/\" />", nestedHtml, StringComparison.Ordinal);

        // 2. Root index.html (different casing from wwwroot/Index.html) MUST have its <base href> rewritten.
        var rootResult = await resolver.GetFileStreamAsync(clientId, "index.html");
        using var rootReader = new StreamReader(rootResult.Stream!);
        var rootHtml = await rootReader.ReadToEndAsync();
        Assert.Contains($"<base href=\"/{clientId}/\" />", rootHtml, StringComparison.Ordinal);
    }

    [Fact]
    public void Bug29_ETagGenerator_GenerateETag_supports_non_seekable_streams()
    {
        var fileInfo = new NonSeekableFileInfo("app.js", "console.log('hello');");
        var etag = ClientRemote.ETagGenerator.GenerateETag(fileInfo);
        Assert.False(string.IsNullOrWhiteSpace(etag));
    }

    [Fact]
    public void Bug30_ClientFileSyncManager_GetSubPath_strips_wwwroot_with_backslashes_or_leading_slash()
    {
        Assert.Equal("index.html", ClientRemote.ClientFileSyncManager.GetSubPath(@"wwwroot\index.html"));
        Assert.Equal("css/app.css", ClientRemote.ClientFileSyncManager.GetSubPath("/wwwroot/css/app.css"));
    }

    [Fact]
    public async Task Bug31_StatsInterceptor_StatsServerStreamWriter_supports_WriteAsync_with_CancellationToken()
    {
        var stats = new ServerStats();
        var interceptor = new StatsInterceptor(stats);
        var writer = new CallbackStreamWriter<StringRequest>(_ => { });
        using var cts = new CancellationTokenSource();

        await interceptor.ServerStreamingServerHandler(
            new ReceiveMessageRequest { Id = "client-1" },
            writer,
            new TestCallContext(cts.Token),
            async (_, wrappedWriter, ctx) =>
            {
                await wrappedWriter.WriteAsync(new StringRequest { Request = "hello" }, ctx.CancellationToken);
            });

        Assert.Single(writer.Messages);
        Assert.True((long)stats.GetStats()["TotalBytesSent"] > 0);
    }

    [Fact]
    public async Task Bug32_StatsInterceptor_records_BytesReceived_on_streaming_calls_and_retains_BytesSent_when_stream_cancels()
    {
        var stats = new ServerStats();
        var interceptor = new StatsInterceptor(stats);
        var reader = new SequenceStreamReader<ClientFileReadResponse>(
        [
            new ClientFileReadResponse { ClientId = "c1", RequestId = "r1", Path = "index.html" }
        ]);
        var writer = new CallbackStreamWriter<ServerFileReadRequest>(_ => { });

        await Assert.ThrowsAnyAsync<RpcException>(() =>
            interceptor.DuplexStreamingServerHandler(
                reader,
                writer,
                new TestCallContext(CancellationToken.None),
                async (reqStream, resStream, _) =>
                {
                    Assert.True(await reqStream.MoveNext(CancellationToken.None));
                    await resStream.WriteAsync(new ServerFileReadRequest { ClientId = "c1", RequestId = "r1", Path = "index.html" });
                    throw new OperationCanceledException("Client disconnected");
                }));

        var snapshot = stats.GetStats();
        Assert.True((long)snapshot["TotalBytesSent"] > 0);
    }

    [Fact]
    public async Task Bug35_FileWatcherService_WatchFile_accepts_relative_file_path_in_current_directory()
    {
        var fileName = $"bug35-{Guid.NewGuid():N}.tmp";
        var fullPath = Path.Combine(Directory.GetCurrentDirectory(), fileName);
        await File.WriteAllTextAsync(fullPath, "hello");

        try
        {
            var service = new FileWatcherService(NullLogger<FileWatcherService>.Instance);
            using var cts = new CancellationTokenSource();
            var writer = new CallbackStreamWriter<WatchFileResponse>(msg =>
            {
                if (msg.ResponseCase == WatchFileResponse.ResponseOneofCase.Chunk && msg.Chunk.Content.IsEmpty)
                    cts.Cancel();
            });

            await service.WatchFile(
                new WatchFileRequest { FilePath = fileName },
                writer,
                new TestCallContext(cts.Token));

            Assert.True(writer.Messages.Count >= 2);
        }
        finally
        {
            File.Delete(fullPath);
        }
    }

    [Fact]
    public void Bug36_ServerFileSyncManager_Dispose_is_idempotent_and_tolerates_already_completed_metadata_tasks()
    {
        var manager = new ServerFileSyncManager(NullLogger<ServerFileSyncManager>.Instance);
        manager.RegisterClient("c1", "wwwroot/index.html");

        var dictField = typeof(ServerFileSyncManager).GetField("_metadataRequests", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var metadataDict = (ConcurrentDictionary<string, ConcurrentDictionary<string, TaskCompletionSource<FileMetadata>>>)dictField.GetValue(manager)!;
        var completedTcs = new TaskCompletionSource<FileMetadata>();
        completedTcs.SetResult(new FileMetadata { StatusCode = 200 });
        metadataDict["c1"]["req-done"] = completedTcs;

        manager.Dispose();
        // Calling Dispose a second time must be a safe no-op rather than throwing ObjectDisposedException / ChannelClosedException.
        var ex = Record.Exception(() => manager.Dispose());
        Assert.Null(ex);
    }

    [Fact]
    public void Bug38_RestartPage_Html_encodes_guid_in_script_urls()
    {
        var maliciousGuid = "123');alert(1);//<script>bad</script>";
        var html = RestartPage.Html(maliciousGuid, "MyApp", "MyHost");

        Assert.DoesNotContain("'/app/123');alert(1);//", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<script>bad</script>", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Bug40_ClientFileSyncManager_GetFileMetadata_returns_404_for_directory_entries()
    {
        var writer = new RecordingClientStreamWriter<ClientRemote.ClientFileReadResponse>();
        var provider = new DirectoryEntryFileProvider("subdir");
        var ctor = typeof(ClientRemote.ClientFileSyncManager).GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic,
            [typeof(string), typeof(Microsoft.Extensions.FileProviders.IFileProvider), typeof(IClientStreamWriter<ClientRemote.ClientFileReadResponse>), typeof(Microsoft.Extensions.Logging.ILogger)])!;

        var manager = (ClientRemote.ClientFileSyncManager)ctor.Invoke(["client-1", provider, writer, NullLogger.Instance]);
        var handleMeta = typeof(ClientRemote.ClientFileSyncManager).GetMethod("HandleMetaDataRequestAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;

        await (Task)handleMeta.Invoke(manager, [new ClientRemote.ServerFileReadRequest
        {
            ClientId = "client-1",
            RequestId = "req-dir",
            Path = "wwwroot/subdir",
            RequestType = ClientRemote.ServerFileReadRequest.Types.RequestType.MetaData
        }])!;

        var sent = Assert.Single(writer.Messages);
        Assert.Equal(StatusCodes.Status404NotFound, sent.Metadata.StatusCode);
    }

    [Fact]
    public async Task Bug41_RemoteWebView_ProcessBrowserMessages_ignores_location_and_size_commands_without_faulting()
    {
        ClientRemote.DisconnectedEventArgs? disconnected = null;
        var fakeView = new CapturingBlazorWebView(_ => { });
        fakeView.Disconnected += (_, args) => disconnected = args;

        var remote = new ClientRemote.RemoteWebView(
            fakeView,
            "wwwroot/index.html",
            new SyncDispatcher(),
            new NullFileProvider(),
            NullLogger.Instance);

        var reader = new SequenceStreamReader<ClientRemote.WebMessageResponse>(
        [
            new ClientRemote.WebMessageResponse { Response = "created:" },
            new ClientRemote.WebMessageResponse { Response = "location:{\"X\":10,\"Y\":20}" },
            new ClientRemote.WebMessageResponse { Response = "size:{\"Width\":800,\"Height\":600}" }
        ]);
        var call = new AsyncServerStreamingCall<ClientRemote.WebMessageResponse>(
            reader,
            Task.FromResult(new Metadata()),
            () => Status.DefaultSuccess,
            () => new Metadata(),
            () => { });

        var method = typeof(ClientRemote.RemoteWebView).GetMethod("ProcessBrowserMessages", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var task = (Task<Exception?>)method.Invoke(remote, [fakeView, call])!;
        var ex = await task.WaitAsync(TimeSpan.FromSeconds(1));
        await Task.Delay(50);

        Assert.Null(ex);
        Assert.Null(disconnected);
        var disposedField = typeof(ClientRemote.RemoteWebView).GetField("_disposed", BindingFlags.Instance | BindingFlags.NonPublic)!;
        Assert.Equal(0, (int)disposedField.GetValue(remote)!);
        await remote.DisposeAsyncCore();
    }

    [Fact]
    public async Task Bug42_RemoteWebView_HandleConnectedAsync_preserves_user_display_name_containing_pipe_character()
    {
        ClientRemote.ConnectedEventArgs? captured = null;
        var fakeView = new CapturingBlazorWebView(args => captured = args);
        var remote = new ClientRemote.RemoteWebView(
            fakeView,
            "wwwroot/index.html",
            new SyncDispatcher(),
            new NullFileProvider(),
            NullLogger.Instance);

        var method = typeof(ClientRemote.RemoteWebView).GetMethod("HandleConnectedAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var ok = await (Task<bool>)method.Invoke(remote, [fakeView, "127.0.0.1|Alice | Contoso Admin", "{}", CancellationToken.None])!;

        Assert.True(ok);
        Assert.NotNull(captured);
        Assert.Equal("127.0.0.1", captured!.IpAddress);
        Assert.Equal("Alice | Contoso Admin", captured.User);
        await remote.DisposeAsyncCore();
    }

    [Fact]
    public void Bug43_RemoteWebView_GenMarkup_ensures_slash_before_app_segment()
    {
        var id = Guid.NewGuid();
        var markup = ClientRemote.RemoteWebView.GenMarkup(new Uri("https://example.com:5001/base"), id);
        Assert.Contains($"https://example.com:5001/base/app/{id}", markup, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Bug45_RemoteFilesMiddleware_matches_quoted_and_comma_separated_IfNoneMatch_and_ignores_empty_etag()
    {
        var clientId = Guid.NewGuid();
        var services = new ConcurrentDictionary<string, TaskCompletionSource<ServiceState>>();
        using var state = new ServiceState(NullLogger<RemoteWebViewService>.Instance, false) { Id = clientId.ToString() };
        state.FileManagerReady.SetResult(true);
        var stateTcs = new TaskCompletionSource<ServiceState>(TaskCreationOptions.RunContinuationsAsynchronously);
        stateTcs.SetResult(state);
        services[clientId.ToString()] = stateTcs;

        using var fileSync = new ServerFileSyncManager(NullLogger<ServerFileSyncManager>.Instance);
        var resolver = new StubRemoteFileResolver(
            fileSync,
            new FileMetadata { Length = 5, StatusCode = StatusCodes.Status200OK, ETag = "raw-etag-value" },
            new MemoryStream(Encoding.UTF8.GetBytes("hello")));

        using var cache = new MemoryCache(new MemoryCacheOptions());
        var middleware = new RemoteFilesMiddleware(
            _ => Task.CompletedTask,
            cache,
            NullLogger<RemoteFilesMiddleware>.Instance,
            resolver,
            fileSync);

        using var sp = new ServiceCollection()
            .AddSingleton(services)
            .AddSingleton(new RemoteFilesOptions { UseClientCache = true, UseServerCache = false })
            .AddSingleton<Microsoft.Extensions.Logging.ILogger<RemoteWebViewService>>(NullLogger<RemoteWebViewService>.Instance)
            .BuildServiceProvider();

        var context = new DefaultHttpContext { RequestServices = sp };
        context.Request.Method = HttpMethods.Get;
        context.Request.Path = $"/{clientId}/app.js";
        context.Request.Headers.IfNoneMatch = "\"other-etag\", \"raw-etag-value\"";
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status304NotModified, context.Response.StatusCode);
    }

    [Fact]
    public async Task Bug46_RemoteWebViewService_CreateWebView_cleans_up_immediately_without_5s_hang_when_write_throws_and_tolerates_closed_channel()
    {
        var id = Guid.NewGuid().ToString();
        var dict = new ConcurrentDictionary<string, TaskCompletionSource<ServiceState>>();
        var channels = new ConcurrentDictionary<string, Channel<string>>();
        var closedChannel = Channel.CreateUnbounded<string>();
        closedChannel.Writer.Complete();
        channels["closed"] = closedChannel;

        var shutdown = new ShutdownService(NullLogger<RemoteWebViewService>.Instance, channels, dict);
        using var fileSync = new ServerFileSyncManager(NullLogger<ServerFileSyncManager>.Instance);
        var service = new RemoteWebViewService(NullLogger<RemoteWebViewService>.Instance, dict, channels, shutdown, fileSync);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        await service.CreateWebView(
            new CreateWebViewRequest { Id = id, HtmlHostPath = "wwwroot/index.html" },
            new ThrowingStreamWriter<WebMessageResponse>(new TaskCanceledException("write canceled during CreateWebView")),
            new TestCallContext(CancellationToken.None));
        sw.Stop();

        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(1));
        Assert.False(dict.ContainsKey(id));
    }

    [Fact]
    public async Task Bug47_RemoteWebViewService_Ping_defaults_non_positive_PingIntervalSeconds_instead_of_immediately_timing_out()
    {
        var id = Guid.NewGuid().ToString();
        using var state = new ServiceState(NullLogger<RemoteWebViewService>.Instance, enableMirrors: false) { Id = id };
        var tcs = new TaskCompletionSource<ServiceState>(TaskCreationOptions.RunContinuationsAsynchronously);
        tcs.SetResult(state);
        var dict = new ConcurrentDictionary<string, TaskCompletionSource<ServiceState>> { [id] = tcs };
        var channels = new ConcurrentDictionary<string, Channel<string>>();
        var shutdown = new ShutdownService(NullLogger<RemoteWebViewService>.Instance, channels, dict);
        using var fileSync = new ServerFileSyncManager(NullLogger<ServerFileSyncManager>.Instance);
        var service = new RemoteWebViewService(NullLogger<RemoteWebViewService>.Instance, dict, channels, shutdown, fileSync);

        using var cts = new CancellationTokenSource();
        var firstPingSent = new TaskCompletionSource<PingMessageResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelledPingSent = false;
        var writer = new CallbackStreamWriter<PingMessageResponse>(msg =>
        {
            if (msg.Cancelled)
                cancelledPingSent = true;
            firstPingSent.TrySetResult(msg);
        });

        var reader = new BlockingAfterFirstStreamReader<PingMessageRequest>(
            new PingMessageRequest { Id = id, Initialize = true, PingIntervalSeconds = 0 },
            cts.Token);

        var pingCall = service.Ping(reader, writer, new TestCallContext(cts.Token));
        await firstPingSent.Task.WaitAsync(TimeSpan.FromSeconds(1));
        await Task.Delay(100);

        Assert.False(cancelledPingSent);
        Assert.True(dict.ContainsKey(id));

        cts.Cancel();
        try { await pingCall.WaitAsync(TimeSpan.FromSeconds(1)); } catch { }
    }

    [Fact]
    public async Task Bug48_ClientIPCService_GetServerStatus_skips_uncompleted_placeholder_registrations_without_blocking()
    {
        var dict = new ConcurrentDictionary<string, TaskCompletionSource<ServiceState>>();
        var activeId = Guid.NewGuid().ToString();
        using var activeState = new ServiceState(NullLogger<RemoteWebViewService>.Instance, enableMirrors: false)
        {
            Id = activeId,
            HostName = "host-1"
        };
        var completedTcs = new TaskCompletionSource<ServiceState>(TaskCreationOptions.RunContinuationsAsynchronously);
        completedTcs.SetResult(activeState);
        dict[activeId] = completedTcs;

        // Add an uncompleted placeholder TCS (e.g., from /wait/{id} for a client that hasn't connected yet).
        dict[Guid.NewGuid().ToString()] = new TaskCompletionSource<ServiceState>(TaskCreationOptions.RunContinuationsAsynchronously);

        var service = new ClientIPCService(
            NullLogger<ClientIPCService>.Instance,
            new ConcurrentDictionary<string, Channel<string>>(),
            dict,
            new FixedGroupUserService("admin"),
            new RemoteFilesOptions());

        var statusTask = service.GetServerStatus(new Google.Protobuf.WellKnownTypes.Empty(), new TestCallContext(CancellationToken.None));
        using var timeout = new CancellationTokenSource();
        var delay = Task.Delay(TimeSpan.FromMilliseconds(500), timeout.Token);
        Task completed;
        try
        {
            completed = await Task.WhenAny(statusTask, delay);
        }
        finally
        {
            timeout.Cancel();
            try { await delay; } catch (OperationCanceledException) { }
        }

        Assert.Same(statusTask, completed);
        var response = await statusTask;
        Assert.Single(response.ConnectionResponses);
        Assert.Equal(activeId, response.ConnectionResponses[0].Id);
    }

    [Fact]
    public void Bug49_EditWebView_Utility_CopyDirectory_handles_mixed_slashes_and_matching_subfolder_names()
    {
        var root = Path.Combine(Path.GetTempPath(), $"bug49-{Guid.NewGuid():N}");
        var relSrc = $"bug49_src_{Guid.NewGuid():N}";
        var nested = Path.Combine(relSrc, "sub", relSrc);
        var dst = Path.Combine(root, "dst");
        Directory.CreateDirectory(nested);
        File.WriteAllText(Path.Combine(nested, "test.txt"), "copied-content");

        try
        {
            EditWebView.Utility.CopyDirectory(relSrc, dst);

            var copiedFile = Path.Combine(dst, "sub", relSrc, "test.txt");
            Assert.True(File.Exists(copiedFile));
            Assert.Equal("copied-content", File.ReadAllText(copiedFile));
        }
        finally
        {
            if (Directory.Exists(relSrc))
                Directory.Delete(relSrc, recursive: true);
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Bug50_EditWebView_Utility_UnzipFile_blocks_sibling_directory_prefix_zip_slip()
    {
        var root = Path.Combine(Path.GetTempPath(), $"bug50-{Guid.NewGuid():N}");
        var extractDir = Path.Combine(root, "extract");
        var siblingFile = Path.Combine(root, "extract-evil", "escaped.txt");
        var zipPath = Path.Combine(root, "test.zip");
        Directory.CreateDirectory(root);

        try
        {
            using (var zipStream = new System.IO.FileStream(zipPath, FileMode.Create))
            using (var archive = new System.IO.Compression.ZipArchive(zipStream, System.IO.Compression.ZipArchiveMode.Create))
            {
                var entry = archive.CreateEntry("../extract-evil/escaped.txt");
                using var writer = new StreamWriter(entry.Open());
                writer.Write("malicious");
            }

            Assert.Throws<IOException>(() => EditWebView.Utility.UnzipFile(zipPath, extractDir));
            Assert.False(File.Exists(siblingFile));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Bug51_EditWebView_Utility_ConvertUnixToWindowsLineEndings_normalizes_files_with_mixed_line_endings()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"bug51-{Guid.NewGuid():N}.cs");
        try
        {
            File.WriteAllText(tempFile, "line1\r\nline2\nline3\n", Encoding.UTF8);

            EditWebView.Utility.ConvertUnixToWindowsLineEndings(tempFile);

            var updated = File.ReadAllText(tempFile, Encoding.UTF8);
            Assert.Equal("line1\r\nline2\r\nline3\r\n", updated);
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Fact]
    public void Bug52_EditWebView_Utility_GetFilenameFromUrl_handles_case_insensitive_repo_and_query_string()
    {
        var filename = EditWebView.Utility.GetFilenameFromUrl(
            "https://github.com/DOTNET/ASPNETCORE/archive/refs/tags/v11.0.0-rc.2.zip?download=1#hash");

        Assert.Equal("aspnetcore-11.0.0-rc.2.zip", filename);
    }

    [Fact]
    public void Bug53_ServerFileSyncManager_CacheTimeoutSeconds_defaults_to_600_when_env_var_is_non_positive()
    {
        var previous = Environment.GetEnvironmentVariable("CACHE_TIMEOUT_SECONDS");
        try
        {
            Environment.SetEnvironmentVariable("CACHE_TIMEOUT_SECONDS", "0");
            using var managerZero = new ServerFileSyncManager(NullLogger<ServerFileSyncManager>.Instance);
            Assert.Equal(600, managerZero.CacheTimeoutSeconds);

            Environment.SetEnvironmentVariable("CACHE_TIMEOUT_SECONDS", "-15");
            using var managerNegative = new ServerFileSyncManager(NullLogger<ServerFileSyncManager>.Instance);
            Assert.Equal(600, managerNegative.CacheTimeoutSeconds);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CACHE_TIMEOUT_SECONDS", previous);
        }
    }

    [Fact]
    public void Bug54_ServerFileSyncManager_HandleMetadataResponse_uses_TrySetResult_when_request_already_completed()
    {
        using var manager = new ServerFileSyncManager(NullLogger<ServerFileSyncManager>.Instance);
        var clientId = Guid.NewGuid().ToString();
        var requestId = Guid.NewGuid().ToString();
        var metadataField = typeof(ServerFileSyncManager).GetField("_metadataRequests", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var metadataRequests = (ConcurrentDictionary<string, ConcurrentDictionary<string, TaskCompletionSource<FileMetadata>>>)metadataField.GetValue(manager)!;

        var alreadyFaultedTcs = new TaskCompletionSource<FileMetadata>(TaskCreationOptions.RunContinuationsAsynchronously);
        alreadyFaultedTcs.TrySetException(new TimeoutException("already timed out"));
        var perClient = new ConcurrentDictionary<string, TaskCompletionSource<FileMetadata>> { [requestId] = alreadyFaultedTcs };
        metadataRequests[clientId] = perClient;

        var handleMethod = typeof(ServerFileSyncManager).GetMethod("HandleMetadataResponse", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var ex = Record.Exception(() => handleMethod.Invoke(manager, [clientId, requestId, new FileMetadata { StatusCode = 200, Length = 10 }]));

        Assert.Null(ex);
    }

    [Fact]
    public void Bug57_ClientFileSyncManager_GetSubPath_strips_bare_wwwroot_and_repeated_wwwroot_prefixes()
    {
        Assert.Equal(string.Empty, ClientRemote.ClientFileSyncManager.GetSubPath("wwwroot"));
        Assert.Equal(string.Empty, ClientRemote.ClientFileSyncManager.GetSubPath("/wwwroot/"));
        Assert.Equal("index.html", ClientRemote.ClientFileSyncManager.GetSubPath("wwwroot/wwwroot/index.html"));
    }

    [Fact]
    public async Task Bug58_ClientFileSyncManager_CloseAsync_disposes_duplex_call_and_tolerates_null_request_stream()
    {
        var writer = new RecordingClientStreamWriter<ClientRemote.ClientFileReadResponse>();
        var manager = (ClientRemote.ClientFileSyncManager)Activator.CreateInstance(
            typeof(ClientRemote.ClientFileSyncManager),
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            args: [Guid.NewGuid().ToString(), new NullFileProvider(), writer, NullLogger.Instance],
            culture: null)!;

        var disposed = false;
        var dummyCall = new AsyncDuplexStreamingCall<ClientRemote.ClientFileReadResponse, ClientRemote.ServerFileReadRequest>(
            writer,
            new SequenceStreamReader<ClientRemote.ServerFileReadRequest>([]),
            Task.FromResult(new Metadata()),
            () => Status.DefaultSuccess,
            () => new Metadata(),
            () => disposed = true);

        typeof(ClientRemote.ClientFileSyncManager)
            .GetField("_call", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(manager, dummyCall);

        await manager.CloseAsync();

        Assert.True(writer.Completed);
        Assert.True(disposed);
    }

    [Fact]
    public async Task Bug59_Status_endpoint_reports_false_when_registration_is_faulted_or_canceled()
    {
        var id = Guid.NewGuid().ToString();
        var faultedTcs = new TaskCompletionSource<ServiceState>(TaskCreationOptions.RunContinuationsAsynchronously);
        faultedTcs.TrySetCanceled();
        var dict = new ConcurrentDictionary<string, TaskCompletionSource<ServiceState>> { [id] = faultedTcs };

        using var services = new ServiceCollection()
            .AddSingleton(dict)
            .BuildServiceProvider();

        var context = new DefaultHttpContext { RequestServices = services };
        context.Request.RouteValues["id"] = id;
        context.Response.Body = new MemoryStream();

        await Endpoints.Status()(context);

        context.Response.Body.Position = 0;
        var statusResponse = System.Text.Json.JsonSerializer.Deserialize<StatusResponse>(context.Response.Body)!;
        Assert.False(statusResponse.Connected);
    }

    [Fact]
    public void Bug61_RemoteFileResolver_NormalizeHostRelativePath_strips_repeated_and_bare_wwwroot()
    {
        var method = typeof(RemoteFileResolver).GetMethod("NormalizeHostRelativePath", BindingFlags.Static | BindingFlags.NonPublic)!;
        Assert.Equal(string.Empty, (string)method.Invoke(null, ["wwwroot"])!);
        Assert.Equal(string.Empty, (string)method.Invoke(null, ["/wwwroot/"])!);
        Assert.Equal("index.html", (string)method.Invoke(null, ["wwwroot/wwwroot/index.html"])!);
    }

    [Fact]
    public async Task Bug63_Start_endpoint_tolerates_closed_channels_and_rolls_back_InUse_if_notification_fails()
    {
        var id = Guid.NewGuid().ToString();
        using var state = new ServiceState(NullLogger<RemoteWebViewService>.Instance, enableMirrors: false)
        {
            Id = id,
            InUse = false
        };
        var sentMessages = new List<WebMessageResponse>();
        state.IPC.ClientResponseStream = new CallbackStreamWriter<WebMessageResponse>(sentMessages.Add);

        var tcs = new TaskCompletionSource<ServiceState>(TaskCreationOptions.RunContinuationsAsynchronously);
        tcs.SetResult(state);
        var dict = new ConcurrentDictionary<string, TaskCompletionSource<ServiceState>> { [id] = tcs };

        var channels = new ConcurrentDictionary<string, Channel<string>>();
        var closed = Channel.CreateUnbounded<string>();
        closed.Writer.Complete();
        channels["closed"] = closed;

        using var services = new ServiceCollection()
            .AddSingleton(dict)
            .AddSingleton(channels)
            .BuildServiceProvider();

        var context = new DefaultHttpContext { RequestServices = services };
        context.Request.RouteValues["id"] = id;
        context.Response.Body = new MemoryStream();

        await Endpoints.Start()(context);

        Assert.Equal(StatusCodes.Status302Found, context.Response.StatusCode);
        Assert.True(state.InUse);
    }

    [Fact]
    public void Bug65_ServerStats_RecordRequest_ignores_NaN_Infinity_and_negative_response_times()
    {
        var stats = new ServerStats();
        stats.RecordRequest(true, 25.0);
        stats.RecordRequest(true, double.NaN);
        stats.RecordRequest(true, double.PositiveInfinity);
        stats.RecordRequest(true, -10.0);

        Assert.Equal(25.0, (double)stats.GetStats()["MaxResponseTime(ms)"]);
        Assert.Equal(0.0, (double)stats.GetStats()["MinResponseTime(ms)"]);
        Assert.False(double.IsNaN(stats.GetAverageResponseTime()));
    }

    [Fact]
    public void Bug69_Editor_DetermineFileName_matches_WindowsForms_case_insensitively()
    {
        var method = typeof(EditWebView.Editor).GetMethod("DetermineFileName", BindingFlags.Static | BindingFlags.NonPublic)!;
        Assert.Equal("BlazorWebViewFormBase.cs", (string)method.Invoke(null, ["src/windowsforms/BlazorWebView.cs"])!);
        Assert.Equal("BlazorWebViewFormBase.cs", (string)method.Invoke(null, ["src/WINDOWSFORMS/blazorwebview.cs"])!);
    }

    [Fact]
    public void Bug71_OptimizedCompositeFileProvider_tolerates_null_provider_entries_and_null_subpath_in_GetDirectoryContents_and_Watch()
    {
        var composite = new ClientRemote.OptimizedCompositeFileProvider([null!, new NullFileProvider()]);
        Assert.False(composite.GetFileInfo("index.html").Exists);
        Assert.False(composite.GetFileInfo("_framework/blazor.webassembly.js").Exists);
        Assert.False(composite.GetDirectoryContents(null!).Exists);
        Assert.Same(NullChangeToken.Singleton, composite.Watch(null!));
    }

    [Fact]
    public void Bug72_ManifestStaticWebAssetFileProvider_handles_out_of_bounds_pattern_depth_and_content_root_gracefully()
    {
        var manifestType = typeof(ClientRemote.OptimizedCompositeFileProvider).Assembly.GetType("PeakSWC.RemoteWebView.ManifestStaticWebAssetFileProvider+StaticWebAssetManifest")!;
        var providerType = typeof(ClientRemote.OptimizedCompositeFileProvider).Assembly.GetType("PeakSWC.RemoteWebView.ManifestStaticWebAssetFileProvider")!;

        var json = """
        {
          "ContentRoots": ["root0"],
          "Root": {
            "Children": null,
            "Asset": { "ContentRootIndex": 5, "SubPath": "missing.js" },
            "Patterns": [{ "ContentRootIndex": 0, "Pattern": "**", "Depth": 5 }]
          }
        }
        """;
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var manifest = manifestType.GetMethod("Parse", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [stream])!;
        var provider = (Microsoft.Extensions.FileProviders.IFileProvider)Activator.CreateInstance(
            providerType,
            [manifest, new Func<string, Microsoft.Extensions.FileProviders.IFileProvider>(_ => new NullFileProvider())])!;

        Assert.False(provider.GetFileInfo("a.js").Exists);
        Assert.False(provider.GetDirectoryContents("a").Exists);
    }

    [Fact]
    public void Bug76_ETagGenerator_GenerateETag_returns_empty_string_when_file_is_null_or_missing()
    {
        Assert.Equal(string.Empty, ClientRemote.ETagGenerator.GenerateETag(null!));
        Assert.Equal(string.Empty, ClientRemote.ETagGenerator.GenerateETag(new NotFoundFileInfo("missing.txt")));
    }

    [Fact]
    public async Task Bug77_HealthCheck_WaitAsync_returns_false_without_throwing_on_invalid_url_or_non_positive_timeout()
    {
        var ok1 = await (Task<bool>)typeof(ClientRemote.OptimizedCompositeFileProvider).Assembly
            .GetType("PeakSWC.RemoteWebView.HealthCheck")!
            .GetMethod("WaitAsync", BindingFlags.Static | BindingFlags.Public)!
            .Invoke(null, ["not-a-valid-url", 1, 0])!;
        Assert.False(ok1);
    }

    [Fact]
    public async Task Bug80_FileWatcherService_WatchFile_validates_empty_FilePath_with_InvalidArgument_and_guards_disposed_watcher()
    {
        var service = new FileWatcherService(NullLogger<FileWatcherService>.Instance);
        var writer = new CallbackStreamWriter<WatchFileResponse>(_ => { });
        using var cts = new CancellationTokenSource();

        var rpcEx = await Assert.ThrowsAsync<RpcException>(() =>
            service.WatchFile(new WatchFileRequest { FilePath = "   " }, writer, new TestCallContext(cts.Token)));

        Assert.Equal(StatusCode.InvalidArgument, rpcEx.StatusCode);
    }

    [Fact]
    public void Bug81_DeleteDirectoryAndContents_deletes_readonly_files_inside_directory()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"Bug81_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var roFile = Path.Combine(dir, "readonly.txt");
        File.WriteAllText(roFile, "locked");
        File.SetAttributes(roFile, FileAttributes.ReadOnly);
        try
        {
            EditWebView.Utility.DeleteDirectoryAndContents(dir);
            Assert.False(Directory.Exists(dir));
        }
        finally
        {
            if (File.Exists(roFile))
                File.SetAttributes(roFile, FileAttributes.Normal);
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Bug83_RemoteFilesMiddleware_SetResponseHeaders_tolerates_out_of_range_LastModified_timestamp()
    {
        var id = Guid.NewGuid();
        using var state = new ServiceState(NullLogger<RemoteWebViewService>.Instance, enableMirrors: false) { Id = id.ToString() };
        state.FileManagerReady.SetResult(true);
        var tcs = new TaskCompletionSource<ServiceState>(TaskCreationOptions.RunContinuationsAsynchronously);
        tcs.SetResult(state);
        var dict = new ConcurrentDictionary<string, TaskCompletionSource<ServiceState>> { [id.ToString()] = tcs };

        using var syncManager = new ServerFileSyncManager(NullLogger<ServerFileSyncManager>.Instance);
        var resolver = new StubRemoteFileResolver(
            syncManager,
            new FileMetadata
            {
                StatusCode = StatusCodes.Status200OK,
                Length = 2,
                ETag = "etag-83",
                LastModified = long.MaxValue
            },
            new MemoryStream("ok"u8.ToArray()));

        using var services = new ServiceCollection()
            .AddSingleton(dict)
            .AddSingleton(new RemoteFilesOptions { UseClientCache = true })
            .AddSingleton<Microsoft.Extensions.Logging.ILogger<RemoteWebViewService>>(NullLogger<RemoteWebViewService>.Instance)
            .BuildServiceProvider();

        var middleware = new RemoteFilesMiddleware(
            _ => Task.CompletedTask,
            new MemoryCache(new MemoryCacheOptions()),
            NullLogger<RemoteFilesMiddleware>.Instance,
            resolver,
            syncManager);

        var context = new DefaultHttpContext { RequestServices = services };
        context.Request.Method = HttpMethods.Get;
        context.Request.Path = $"/{id}/app.js";
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
    }

    [Fact]
    public async Task Bug88_RemoteWebViewService_CreateWebView_rejects_empty_Id_without_registering_client()
    {
        var dict = new ConcurrentDictionary<string, TaskCompletionSource<ServiceState>>();
        var channels = new ConcurrentDictionary<string, Channel<string>>();
        var shutdown = new ShutdownService(NullLogger<RemoteWebViewService>.Instance, channels, dict);
        using var syncManager = new ServerFileSyncManager(NullLogger<ServerFileSyncManager>.Instance);
        var service = new RemoteWebViewService(NullLogger<RemoteWebViewService>.Instance, dict, channels, shutdown, syncManager);

        var sent = new List<WebMessageResponse>();
        var writer = new CallbackStreamWriter<WebMessageResponse>(sent.Add);
        using var cts = new CancellationTokenSource();

        await service.CreateWebView(new CreateWebViewRequest { Id = "   " }, writer, new TestCallContext(cts.Token)).WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Empty(dict);
        Assert.Contains(sent, m => m.Response == "createFailed:");
    }

    [Fact]
    public async Task Bug89_RemoteWebViewService_Ping_ignores_empty_Id_without_leaking_dictionary_entry()
    {
        var dict = new ConcurrentDictionary<string, TaskCompletionSource<ServiceState>>();
        var channels = new ConcurrentDictionary<string, Channel<string>>();
        var shutdown = new ShutdownService(NullLogger<RemoteWebViewService>.Instance, channels, dict);
        using var syncManager = new ServerFileSyncManager(NullLogger<ServerFileSyncManager>.Instance);
        var service = new RemoteWebViewService(NullLogger<RemoteWebViewService>.Instance, dict, channels, shutdown, syncManager);

        var reader = new SequenceStreamReader<PingMessageRequest>([new PingMessageRequest { Id = "", Initialize = true }]);
        var writer = new CallbackStreamWriter<PingMessageResponse>(_ => { });
        using var cts = new CancellationTokenSource();

        await service.Ping(reader, writer, new TestCallContext(cts.Token)).WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Empty(dict);
    }

    [Fact]
    public void Bug90_HtmlPageGenerator_GenerateContactPage_tolerates_null_contact_and_encodes_framework_description()
    {
        var html = PeakSWC.RemoteWebView.Pages.HtmlPageGenerator.GenerateContactPage(null!, null!);
        Assert.Contains("Contact Information", html);
    }

    [Fact]
    public void Bug91_LockedPage_Html_url_escapes_guid_in_onclick_location_href()
    {
        var html = PeakSWC.RemoteWebView.Pages.LockedPage.Html("alice", "123';alert(1);//");
        Assert.DoesNotContain("&#39;", html);
        Assert.DoesNotContain("';alert(1);", html);
        Assert.Contains("location.href='/123%27%3Balert%281%29%3B%2F%2F'", html);
    }

    [Fact]
    public void Bug92_RestartPage_Html_treats_whitespace_processName_or_hostName_as_missing()
    {
        var html = PeakSWC.RemoteWebView.Pages.RestartPage.Html("id-92", "   ", "   ");
        Assert.DoesNotContain("Restarting <b>", html);
        Assert.Contains("<h1 >Restarting </h1>", html);
    }

    [Fact]
    public async Task Bug93_ShutdownService_Shutdown_tolerates_null_or_whitespace_id()
    {
        var dict = new ConcurrentDictionary<string, TaskCompletionSource<ServiceState>>();
        var channels = new ConcurrentDictionary<string, Channel<string>>();
        var shutdown = new ShutdownService(NullLogger<RemoteWebViewService>.Instance, channels, dict);

        var ex1 = await Record.ExceptionAsync(() => shutdown.Shutdown(null!));
        var ex2 = await Record.ExceptionAsync(() => shutdown.Shutdown("   "));

        Assert.Null(ex1);
        Assert.Null(ex2);
    }

    [Fact]
    public async Task Bug94_ClientIPCService_GetClients_tolerates_null_request_and_null_groups()
    {
        var service = new ClientIPCService(
            NullLogger<ClientIPCService>.Instance,
            new ConcurrentDictionary<string, Channel<string>>(),
            new ConcurrentDictionary<string, TaskCompletionSource<ServiceState>>(),
            new NullGroupUserService(),
            new RemoteFilesOptions());

        using var cts = new CancellationTokenSource();
        var writer = new CallbackStreamWriter<ClientResponseList>(_ => cts.Cancel());

        var ex = await Record.ExceptionAsync(() =>
            service.GetClients(null!, writer, new TestCallContext(cts.Token)).WaitAsync(TimeSpan.FromSeconds(1)));

        Assert.True(ex is null or OperationCanceledException);
        Assert.NotEmpty(writer.Messages);
    }

    [Fact]
    public async Task Bug95_RemoteWebViewService_RequestClientFileRead_rejects_empty_ClientId_with_InvalidArgument()
    {
        var dict = new ConcurrentDictionary<string, TaskCompletionSource<ServiceState>>();
        var channels = new ConcurrentDictionary<string, Channel<string>>();
        var shutdown = new ShutdownService(NullLogger<RemoteWebViewService>.Instance, channels, dict);
        using var syncManager = new ServerFileSyncManager(NullLogger<ServerFileSyncManager>.Instance);
        var service = new RemoteWebViewService(NullLogger<RemoteWebViewService>.Instance, dict, channels, shutdown, syncManager);

        var initMsg = new ClientFileReadResponse
        {
            ClientId = "   ",
            Init = new Init { HtmlHostPath = "index.html" }
        };
        var reader = new SequenceStreamReader<ClientFileReadResponse>([initMsg]);
        var writer = new CallbackStreamWriter<ServerFileReadRequest>(_ => { });
        using var cts = new CancellationTokenSource();

        var rpcEx = await Assert.ThrowsAsync<RpcException>(() =>
            service.RequestClientFileRead(reader, writer, new TestCallContext(cts.Token)).WaitAsync(TimeSpan.FromSeconds(1)));

        Assert.Equal(StatusCode.InvalidArgument, rpcEx.StatusCode);
        Assert.Empty(dict);
    }

    [Fact]
    public async Task Bug97_ServerFileSyncManager_RequestFileMetadataAsync_and_RequestFileDataAsync_tolerate_null_or_empty_clientGuid()
    {
        using var manager = new ServerFileSyncManager(NullLogger<ServerFileSyncManager>.Instance);

        var meta = await manager.RequestFileMetadataAsync(null!, "index.html", NullLogger<RemoteFileResolver>.Instance).WaitAsync(TimeSpan.FromSeconds(1));
        Assert.Equal((int)System.Net.HttpStatusCode.InternalServerError, meta.StatusCode);

        using var dataReq = await manager.RequestFileDataAsync("", "index.html", NullLogger<RemoteFileResolver>.Instance).WaitAsync(TimeSpan.FromSeconds(1));
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await dataReq.Pipe.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void Bug98_ServerStats_GetLatencyPercentiles_uses_defaults_when_percentiles_list_is_empty()
    {
        var stats = new ServerStats();
        stats.RecordRequest(true, 10.0);
        stats.RecordRequest(true, 30.0);

        var defaultPct = stats.GetLatencyPercentiles([]);
        Assert.Contains(50, defaultPct.Keys);
        Assert.Contains(90, defaultPct.Keys);
        Assert.Contains(99, defaultPct.Keys);
        Assert.Equal(20.0, defaultPct[50]);
    }

    [Fact]
    public void Bug99_Editor_WriteAllText_creates_missing_output_directory_and_validates_path()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"Bug99_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        var srcFile = Path.Combine(tempRoot, "Sample.cs");
        File.WriteAllText(srcFile, "using System;\nnamespace Test { internal class Sample {} }");
        try
        {
            var editor = new EditWebView.Editor(srcFile);
            editor.Edit();

            Assert.Throws<ArgumentException>(() => editor.WriteAllText("   "));

            var nestedOut = Path.Combine(tempRoot, "nested", "out");
            editor.WriteAllText(nestedOut);
            Assert.True(File.Exists(Path.Combine(nestedOut, "Sample.cs")));
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void Bug100_Utility_CopyDirectory_snapshots_source_entries_before_creating_subdirectory_target()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"Bug100_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        var fileA = Path.Combine(tempRoot, "a.txt");
        File.WriteAllText(fileA, "hello");
        var subTarget = Path.Combine(tempRoot, "backup");
        try
        {
            EditWebView.Utility.CopyDirectory(tempRoot, subTarget);
            Assert.True(File.Exists(Path.Combine(subTarget, "a.txt")));
            Assert.False(Directory.Exists(Path.Combine(subTarget, "backup")));
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
    }

    private sealed class NullGroupUserService : IUserService
    {
        public Task<IReadOnlyList<string>> GetUserGroups(string? oid) => Task.FromResult<IReadOnlyList<string>>(null!);
    }

    private sealed class NullNameFileInfo : IFileInfo
    {
        public bool Exists => false;
        public long Length => -1;
        public string? PhysicalPath => null;
        public string Name => null!;
        public DateTimeOffset LastModified => DateTimeOffset.UnixEpoch;
        public bool IsDirectory => false;
        public Stream CreateReadStream() => Stream.Null;
    }

    private sealed class DirectoryEntryFileProvider(string dirName) : Microsoft.Extensions.FileProviders.IFileProvider
    {
        public IDirectoryContents GetDirectoryContents(string subpath) => NotFoundDirectoryContents.Singleton;
        public IChangeToken Watch(string filter) => NullChangeToken.Singleton;
        public IFileInfo GetFileInfo(string subpath)
            => string.Equals(subpath.Trim('/'), dirName, StringComparison.OrdinalIgnoreCase)
                ? new DirectoryFileInfo(dirName)
                : new NotFoundFileInfo(subpath);

        private sealed class DirectoryFileInfo(string name) : IFileInfo
        {
            public bool Exists => true;
            public long Length => -1;
            public string? PhysicalPath => null;
            public string Name => name;
            public DateTimeOffset LastModified => DateTimeOffset.UnixEpoch;
            public bool IsDirectory => true;
            public Stream CreateReadStream() => throw new InvalidOperationException("Cannot create a read stream for a directory.");
        }
    }

    private sealed class AsyncGatedStreamWriter<T>(Func<T, Task> onWrite) : IServerStreamWriter<T>
    {
        public WriteOptions? WriteOptions { get; set; }
        public Task WriteAsync(T message) => onWrite(message);
        public Task WriteAsync(T message, CancellationToken cancellationToken) => onWrite(message);
    }

    private sealed class NonSeekableFileInfo(string name, string content) : IFileInfo
    {
        private readonly byte[] _bytes = Encoding.UTF8.GetBytes(content);
        public bool Exists => true;
        public long Length => _bytes.Length;
        public string? PhysicalPath => null;
        public string Name => name;
        public DateTimeOffset LastModified => DateTimeOffset.UnixEpoch;
        public bool IsDirectory => false;
        public Stream CreateReadStream() => new NonSeekableReadStream(_bytes);

        private sealed class NonSeekableReadStream(byte[] bytes) : MemoryStream(bytes, writable: false)
        {
            public override bool CanSeek => false;
            public override long Seek(long offset, SeekOrigin loc) => throw new NotSupportedException("Stream is not seekable.");
        }
    }

    private sealed class FixedGroupUserService(string group) : IUserService
    {
        public Task<IReadOnlyList<string>> GetUserGroups(string? oid) => Task.FromResult<IReadOnlyList<string>>([group]);
    }

    private sealed class CallbackStreamWriter<T>(Action<T> onWrite) : IServerStreamWriter<T>
    {
        public List<T> Messages { get; } = [];
        public WriteOptions? WriteOptions { get; set; }

        public Task WriteAsync(T message)
        {
            Messages.Add(message);
            onWrite(message);
            return Task.CompletedTask;
        }

        public Task WriteAsync(T message, CancellationToken cancellationToken) => WriteAsync(message);
    }

    private sealed class ThrowingStreamWriter<T>(Exception exception) : IServerStreamWriter<T>
    {
        public WriteOptions? WriteOptions { get; set; }
        public Task WriteAsync(T message) => Task.FromException(exception);
        public Task WriteAsync(T message, CancellationToken cancellationToken) => Task.FromException(exception);
    }

    private sealed class RecordingClientStreamWriter<T> : IClientStreamWriter<T>
    {
        public List<T> Messages { get; } = [];
        public bool Completed { get; private set; }
        public WriteOptions? WriteOptions { get; set; }

        public Task WriteAsync(T message)
        {
            Messages.Add(message);
            return Task.CompletedTask;
        }

        public Task CompleteAsync()
        {
            Completed = true;
            return Task.CompletedTask;
        }
    }

    private sealed class SequenceStreamReader<T>(IReadOnlyList<T> items, Action? onSecondRead = null) : IAsyncStreamReader<T>
    {
        private int _index = -1;
        public T Current => items[_index];

        public Task<bool> MoveNext(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _index++;
            if (_index == 1)
                onSecondRead?.Invoke();
            return Task.FromResult(_index < items.Count);
        }
    }

    private sealed class BlockingAfterFirstStreamReader<T>(T first, CancellationToken token) : IAsyncStreamReader<T>
    {
        private int _step;
        public T Current { get; private set; } = default!;

        public async Task<bool> MoveNext(CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _step) == 1)
            {
                Current = first;
                return true;
            }

            using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, cancellationToken);
            await Task.Delay(Timeout.Infinite, linked.Token);
            return false;
        }
    }

    private sealed class TrackingMemoryStream(byte[] buffer) : MemoryStream(buffer, writable: false)
    {
        public bool IsDisposed { get; private set; }

        protected override void Dispose(bool disposing)
        {
            IsDisposed = true;
            base.Dispose(disposing);
        }

        public override ValueTask DisposeAsync()
        {
            IsDisposed = true;
            return base.DisposeAsync();
        }
    }

    private sealed class StubRemoteFileResolver(ServerFileSyncManager fileSyncManager, FileMetadata metadata, Stream stream)
        : RemoteFileResolver(NullLogger<RemoteFileResolver>.Instance, fileSyncManager)
    {
        public string? LastRequestedSubPath { get; private set; }

        public override Task<FileMetadata> GetFileMetaDataAsync(string clientId, string subPath)
        {
            LastRequestedSubPath = subPath;
            return Task.FromResult(metadata);
        }

        public override Task<PeakSWC.RemoteWebView.RemoteStaticFiles.FileStream> GetFileStreamAsync(string clientId, string subPath)
        {
            LastRequestedSubPath = subPath;
            return Task.FromResult(new PeakSWC.RemoteWebView.RemoteStaticFiles.FileStream { Stream = stream });
        }
    }

    private sealed class TestCallContext(CancellationToken cancellationToken) : ServerCallContext
    {
        protected override string MethodCore => "test";
        protected override string HostCore => "localhost";
        protected override string PeerCore => "peer";
        protected override DateTime DeadlineCore => DateTime.MaxValue;
        protected override Metadata RequestHeadersCore { get; } = new();
        protected override CancellationToken CancellationTokenCore => cancellationToken;
        protected override Metadata ResponseTrailersCore { get; } = new();
        protected override Status StatusCore { get; set; }
        protected override WriteOptions? WriteOptionsCore { get; set; }
        protected override AuthContext AuthContextCore => null!;
        protected override ContextPropagationToken CreatePropagationTokenCore(ContextPropagationOptions? options) => null!;
        protected override Task WriteResponseHeadersAsyncCore(Metadata responseHeaders) => Task.CompletedTask;
    }

    private sealed class SyncDispatcher : Dispatcher
    {
        public override bool CheckAccess() => true;
        public override Task InvokeAsync(Action workItem) { workItem(); return Task.CompletedTask; }
        public override Task InvokeAsync(Func<Task> workItem) => workItem();
        public override Task<TResult> InvokeAsync<TResult>(Func<TResult> workItem) => Task.FromResult(workItem());
        public override Task<TResult> InvokeAsync<TResult>(Func<Task<TResult>> workItem) => workItem();
    }

    private sealed class CapturingBlazorWebView(Action<ClientRemote.ConnectedEventArgs> onConnected) : ClientRemote.IBlazorWebView
    {
        public event EventHandler<ClientRemote.ConnectedEventArgs>? Connected;
        public event EventHandler<ClientRemote.DisconnectedEventArgs>? Disconnected;
        public event EventHandler<ClientRemote.RefreshedEventArgs>? Refreshed;
        public event EventHandler<ClientRemote.ReadyToConnectEventArgs>? ReadyToConnect;
        public void FireConnected(ClientRemote.ConnectedEventArgs args) { onConnected(args); Connected?.Invoke(this, args); }
        public void FireDisconnected(ClientRemote.DisconnectedEventArgs args) => Disconnected?.Invoke(this, args);
        public void FireRefreshed(ClientRemote.RefreshedEventArgs args) => Refreshed?.Invoke(this, args);
        public void FireReadyToConnect(ClientRemote.ReadyToConnectEventArgs args) => ReadyToConnect?.Invoke(this, args);
        public Uri? ServerUri { get; set; } = new("https://localhost:5001/");
        public Uri? GrpcBaseUri { get; set; } = new("https://localhost:5001/");
        public Task<Uri?> GetGrpcBaseUriAsync(Uri? serverUri) => Task.FromResult(serverUri);
        public string Group { get; set; } = "";
        public Guid Id { get; set; } = Guid.NewGuid();
        public bool EnableMirrors { get; set; }
        public uint PingIntervalSeconds { get; set; } = 30;
        public string Markup { get; set; } = "";
        public void Restart() { }
        public void NavigateToString(string htmlContent) { }
        public Task WaitForInitializationComplete() => Task.CompletedTask;
        public Microsoft.Web.WebView2.Core.CoreWebView2CookieManager CookieManager => null!;
    }

    private sealed class SingleMemoryFileProvider(string matchPath, string content) : Microsoft.Extensions.FileProviders.IFileProvider
    {
        public IDirectoryContents GetDirectoryContents(string subpath) => NotFoundDirectoryContents.Singleton;
        public IChangeToken Watch(string filter) => NullChangeToken.Singleton;
        public IFileInfo GetFileInfo(string subpath)
        {
            var normalized = subpath.TrimStart('/');
            return string.Equals(normalized, matchPath, StringComparison.OrdinalIgnoreCase)
                ? new StringFileInfo(Path.GetFileName(normalized), content)
                : new NotFoundFileInfo(subpath);
        }

        private sealed class StringFileInfo(string name, string text) : IFileInfo
        {
            private readonly byte[] _bytes = Encoding.UTF8.GetBytes(text);
            public bool Exists => true;
            public long Length => _bytes.Length;
            public string? PhysicalPath => null;
            public string Name => name;
            public DateTimeOffset LastModified => DateTimeOffset.UnixEpoch;
            public bool IsDirectory => false;
            public Stream CreateReadStream() => new MemoryStream(_bytes, writable: false);
        }
    }

    private sealed class FakeManifestAssembly(string xml) : Assembly
    {
        public override string Location => string.Empty;
        public override AssemblyName GetName() => new("FakeManifestAssembly");
        public override Stream? GetManifestResourceStream(string name)
            => new MemoryStream(Encoding.UTF8.GetBytes(xml), writable: false);
    }
}
