extern alias client;

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using FileProvider = Microsoft.Extensions.FileProviders.IFileProvider;
using FileInfo = Microsoft.Extensions.FileProviders.IFileInfo;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Primitives;
using PeakSWC.RemoteWebView;
using PeakSWC.RemoteWebView.RemoteStaticFiles;
using PeakSWC.RemoteWebView.Services;
using ClientSync = client::PeakSWC.RemoteWebView.ClientFileSyncManager;

namespace StaticContentCache.EndToEnd.Tests;

public class BaselineModuleRunTests : IAsyncLifetime
{
    private const string ModuleName = "Baseline.lib.module.js";
    private const string HostPageName = "module-init-host.html";
    private static readonly byte[] HostPage = File.ReadAllBytes(Asset(HostPageName));

    private readonly Guid _clientId = Guid.NewGuid();
    private readonly ConcurrentQueue<string> _requests = new();
    private RunHost? _host;
    private GrpcChannel? _channel;
    private ClientSync? _sync;

    public async Task InitializeAsync()
    {
        AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);
        var moduleBytes = File.ReadAllBytes(Asset(ModuleName));
        var manifestBytes = File.ReadAllBytes(Asset("blazor.modules.json"));
        var hookBytes = File.ReadAllBytes(Asset("module-init-hook.js"));
        var files = new MemoryFileProvider(new Dictionary<string, byte[]>
        {
            ["_framework/blazor.modules.json"] = manifestBytes,
            [ModuleName] = moduleBytes,
            ["module-init-hook.js"] = hookBytes,
            [HostPageName] = HostPage
        });

        _host = await RunHost.StartAsync(_clientId, _requests);
        _channel = GrpcChannel.ForAddress(_host.GrpcUrl, new GrpcChannelOptions
        {
            HttpHandler = new SocketsHttpHandler { EnableMultipleHttp2Connections = true, UseCookies = false }
        });
        var ipc = new client::PeakSWC.RemoteWebView.WebViewIPC.WebViewIPCClient(_channel);
        _sync = new ClientSync(ipc, _clientId, HostPageName, files, static _ => { }, NullLogger.Instance);
        _sync.HandleServerRequests(CancellationToken.None);
        await _host.WaitUntilReady();
    }

    public async Task DisposeAsync()
    {
        if (_sync is not null)
            await _sync.CloseAsync();
        _channel?.Dispose();
        if (_host is not null)
            await _host.DisposeAsync();
    }

    [HeadlessChromeFact]
    public async Task Test_hook_sees_each_initializer_once_in_order_via_the_remote_path()
    {
        var chrome = ChromeSession.FindBrowser()
            ?? throw new InvalidOperationException("Google Chrome or Chromium was not found.");
        var pageUrl = $"{_host!.BrowserUrl}/{_clientId}/{HostPageName}";
        var json = await RunHook(chrome, pageUrl);
        var report = JsonSerializer.Deserialize<HookReport>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.NotNull(report);
        Assert.True(string.IsNullOrEmpty(report.Error), report.Error);
        var names = Assert.IsType<string[]>(report.Names);
        var fetched = Assert.IsType<string[]>(report.Fetched);
        var steps = Assert.IsType<HookStep[]>(report.Steps);
        Assert.Equal([ModuleName], names);
        Assert.EndsWith($"/{_clientId}/", report.BaseURI);
        Assert.Equal([$"/{_clientId}/{ModuleName}"], fetched);

        Assert.Equal(2, steps.Length);
        Assert.Equal("beforeStart", steps[0].Hook);
        Assert.Equal(1, steps[0].State.BeforeStart);
        Assert.Equal(0, steps[0].State.AfterStarted);
        Assert.Equal("afterStarted", steps[1].Hook);
        Assert.Equal(1, steps[1].State.BeforeStart);
        Assert.Equal(1, steps[1].State.AfterStarted);
        Assert.Equal(1, report.Final.BeforeStart);
        Assert.Equal(1, report.Final.AfterStarted);

        var paths = _requests.ToArray();
        Assert.Contains($"/{_clientId}/_framework/blazor.modules.json", paths);
        Assert.Contains($"/{_clientId}/{ModuleName}", paths);
        Assert.Contains($"/{_clientId}/module-init-hook.js", paths);
    }

    private static string Asset(string name) => Path.Combine(AppContext.BaseDirectory, "Assets", name);

    private async Task<string> RunHook(string chrome, string url)
    {
        var profile = Path.Combine(Path.GetTempPath(), "module-init-chrome-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(profile);
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = chrome,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        process.StartInfo.ArgumentList.Add("--headless=new");
        process.StartInfo.ArgumentList.Add("--no-sandbox");
        process.StartInfo.ArgumentList.Add("--disable-gpu");
        process.StartInfo.ArgumentList.Add("--disable-dev-shm-usage");
        process.StartInfo.ArgumentList.Add("--user-data-dir=" + profile);
        process.StartInfo.ArgumentList.Add(url);
        process.Start();
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        var deadline = DateTime.UtcNow.AddSeconds(15);
        string? result = null;
        while (DateTime.UtcNow < deadline)
        {
            result = _requests.Select(static path => Regex.Match(path, @"module-init-result\?report=([^&]*)"))
                .Where(static match => match.Success)
                .Select(static match => Uri.UnescapeDataString(match.Groups[1].Value))
                .LastOrDefault();
            if (result is not null)
                break;
            await Task.Delay(50);
        }
        if (!process.HasExited)
            process.Kill(entireProcessTree: true);
        await Task.WhenAll(stdout, stderr);
        try
        {
            Directory.Delete(profile, recursive: true);
        }
        catch (IOException)
        {
        }
        Assert.NotNull(result);
        return result;
    }

    private sealed record HookReport(string? Error, string BaseURI, string[]? Names, string[]? Fetched, HookStep[]? Steps, HookState Final);
    private sealed record HookStep(string Hook, HookState State);
    private sealed record HookState(int BeforeStart, int AfterStarted);

    private sealed class RunHost : IAsyncDisposable
    {
        private readonly WebApplication _app;
        private readonly TaskCompletionSource<bool> _ready;
        public string GrpcUrl { get; }
        public string BrowserUrl { get; }

        private RunHost(WebApplication app, string grpcUrl, string browserUrl, TaskCompletionSource<bool> ready)
        {
            _app = app;
            GrpcUrl = grpcUrl;
            BrowserUrl = browserUrl;
            _ready = ready;
        }

        public static async Task<RunHost> StartAsync(Guid clientId, ConcurrentQueue<string> requests)
        {
            var ready = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var state = new ServiceState(NullLogger<RemoteWebViewService>.Instance, false);
            state.FileManagerReady = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _ = state.FileManagerReady.Task.ContinueWith(task =>
            {
                if (task.IsCompletedSuccessfully)
                    ready.TrySetResult(task.Result);
                else if (task.Exception is not null)
                    ready.TrySetException(task.Exception);
            }, TaskScheduler.Default);
            var source = new TaskCompletionSource<ServiceState>(TaskCreationOptions.RunContinuationsAsynchronously);
            source.SetResult(state);
            var states = new ConcurrentDictionary<string, TaskCompletionSource<ServiceState>>
            {
                [clientId.ToString()] = source
            };
            var grpcPort = FreePort();
            var browserPort = FreePort();

            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseUrls();
            builder.WebHost.ConfigureKestrel(options =>
            {
                options.Listen(IPAddress.Loopback, grpcPort, listen => listen.Protocols = HttpProtocols.Http2);
                options.Listen(IPAddress.Loopback, browserPort, listen => listen.Protocols = HttpProtocols.Http1);
            });
            builder.Services.AddGrpc();
            builder.Services.AddLogging();
            builder.Services.AddMemoryCache();
            builder.Services.AddSingleton(states);
            builder.Services.AddSingleton(new ConcurrentDictionary<string, Channel<string>>());
            builder.Services.AddSingleton(new RemoteFilesOptions { UseClientCache = false, UseServerCache = false });
            builder.Services.AddSingleton<ServerFileSyncManager>();
            builder.Services.AddSingleton<RemoteFileResolver>();
            builder.Services.AddSingleton<ShutdownService>();
            builder.Services.AddSingleton<RemoteWebViewService>();
            var app = builder.Build();
            app.Use(async (context, next) =>
            {
                requests.Enqueue((context.Request.Path.Value ?? string.Empty) + context.Request.QueryString.Value);
                await next();
            });
            app.UseMiddleware<RemoteFilesMiddleware>();
            app.MapGrpcService<RemoteWebViewService>();
            await app.StartAsync();
            return new RunHost(app, $"http://127.0.0.1:{grpcPort}", $"http://127.0.0.1:{browserPort}", ready);
        }

        public async Task WaitUntilReady()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await _ready.Task.WaitAsync(timeout.Token);
        }

        public async ValueTask DisposeAsync()
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }

        private static int FreePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }
    }

    private sealed class MemoryFileProvider(Dictionary<string, byte[]> files) : FileProvider
    {
        public IDirectoryContents GetDirectoryContents(string subpath) => NotFoundDirectoryContents.Singleton;
        public IChangeToken Watch(string filter) => NullChangeToken.Singleton;
        public FileInfo GetFileInfo(string subpath)
        {
            var key = subpath.TrimStart('/');
            return files.TryGetValue(key, out var bytes) ? new MemoryFile(key, bytes) : new NotFoundFileInfo(key);
        }
    }

    private sealed class MemoryFile(string path, byte[] bytes) : FileInfo
    {
        public bool Exists => true;
        public long Length => bytes.Length;
        public string? PhysicalPath => null;
        public string Name => Path.GetFileName(path);
        public DateTimeOffset LastModified { get; } = DateTimeOffset.UnixEpoch;
        public bool IsDirectory => false;
        public Stream CreateReadStream() => new MemoryStream(bytes, writable: false);
    }
}
