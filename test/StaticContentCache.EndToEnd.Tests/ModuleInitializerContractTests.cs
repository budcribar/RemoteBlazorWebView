extern alias client;

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
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
using PeakSWC.RemoteWebView.Services;
using ClientSync = client::PeakSWC.RemoteWebView.ClientFileSyncManager;

namespace StaticContentCache.EndToEnd.Tests;

[Collection("HeadlessChrome")]
public partial class ModuleInitializerContractTests
{
    private const string HostPageName = "module-init-contract-host.html";
    private const string HookName = "module-init-contract-hook.js";
    private static readonly byte[] HostPage = File.ReadAllBytes(Asset(HostPageName));
    private static readonly byte[] HookScript = File.ReadAllBytes(Asset(HookName));
    private static readonly byte[] RecordModule = File.ReadAllBytes(Asset("Record.lib.module.js"));
    private static readonly byte[] SlowModule = File.ReadAllBytes(Asset("Slow.lib.module.js"));
    private static readonly byte[] ThrowingModule = File.ReadAllBytes(Asset("Throwing.lib.module.js"));
    private static readonly string[] BlazorKeys = ["_internal", "navigateTo", "registerCustomEventType", "rootComponents", "runtime"];
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    [HeadlessChromeFact]
    public async Task Two_initializers_each_run_once_in_listed_manifest_order()
    {
        var id = Guid.NewGuid();
        var files = Page(["First.lib.module.js", "Second.lib.module.js"],
            ("First.lib.module.js", RecordModule),
            ("Second.lib.module.js", RecordModule));
        await using var session = await ContractSession.StartAsync([new ClientFiles(id, files)]);
        var report = await session.LoadAsync(id, "");

        Assert.True(string.IsNullOrEmpty(report.StageError), report.StageError);
        var names = Assert.IsType<string[]>(report.Names);
        Assert.Equal(["First.lib.module.js", "Second.lib.module.js"], names);
        Assert.EndsWith($"/{id}/", report.BaseURI);
        AssertOnce(report, $"/{id}/First.lib.module.js");
        AssertOnce(report, $"/{id}/Second.lib.module.js");
        Assert.Equal(
            [$"/{id}/First.lib.module.js", $"/{id}/Second.lib.module.js"],
            names.Select(name => $"/{id}/{name}").ToArray());
    }

    [HeadlessChromeFact]
    public async Task Hooks_receive_webview_arguments_and_async_beforeStart_finishes_before_afterStarted()
    {
        var id = Guid.NewGuid();
        var files = Page(["Slow.lib.module.js", "Record.lib.module.js"],
            ("Slow.lib.module.js", SlowModule),
            ("Record.lib.module.js", RecordModule));
        await using var session = await ContractSession.StartAsync([new ClientFiles(id, files)]);
        var report = await session.LoadAsync(id, "");
        var log = Assert.IsType<LogEntry[]>(report.Log);

        Assert.True(string.IsNullOrEmpty(report.StageError), report.StageError);
        var before = log.Where(entry => entry.Hook == "beforeStart" && entry.Phase != "leave").ToArray();
        Assert.Equal(2, before.Length);
        Assert.All(before, entry => Assert.Equal(0, entry.Argc));
        var after = log.Where(entry => entry.Hook == "afterStarted").ToArray();
        Assert.Equal(2, after.Length);
        Assert.All(after, entry =>
        {
            Assert.Equal(1, entry.Argc);
            Assert.Equal(BlazorKeys, entry.Keys);
        });
        var leaveAt = Array.FindIndex(log, entry => entry.Marker == "slow" && entry.Phase == "leave");
        Assert.True(leaveAt >= 0);
        Assert.All(log.Select((entry, index) => (entry, index)).Where(item => item.entry.Hook == "afterStarted"),
            item => Assert.True(item.index > leaveAt));

        var manifest = session.Requests.First(request => request.Path == $"/{id}/_framework/blazor.modules.json");
        var hook = session.Requests.First(request => request.Path == $"/{id}/{HookName}");
        Assert.Equal("max-age=0", manifest.CacheControl);
        Assert.Equal("", hook.CacheControl);
    }

    [HeadlessChromeFact]
    public async Task Throwing_hook_leaves_the_other_beforeStart_and_stops_before_afterStarted()
    {
        var id = Guid.NewGuid();
        var files = Page(["Ok.lib.module.js", "Throwing.lib.module.js"],
            ("Ok.lib.module.js", RecordModule),
            ("Throwing.lib.module.js", ThrowingModule));
        await using var session = await ContractSession.StartAsync([new ClientFiles(id, files)]);
        var report = await session.LoadAsync(id, "");
        var log = Assert.IsType<LogEntry[]>(report.Log);

        Assert.Contains("initializer failed", report.StageError);
        Assert.Contains(log, entry => entry.Module == $"/{id}/Ok.lib.module.js" && entry.Hook == "beforeStart");
        Assert.Contains(log, entry => entry.Module == $"/{id}/Throwing.lib.module.js" && entry.Hook == "beforeStart");
        Assert.DoesNotContain(log, entry => entry.Hook == "afterStarted");
    }

    [HeadlessChromeFact]
    public async Task Missing_module_leaves_the_other_beforeStart_and_stops_before_afterStarted()
    {
        var id = Guid.NewGuid();
        var files = Page(["Ok.lib.module.js", "Missing.lib.module.js"], ("Ok.lib.module.js", RecordModule));
        await using var session = await ContractSession.StartAsync([new ClientFiles(id, files)]);
        var report = await session.LoadAsync(id, "");
        var log = Assert.IsType<LogEntry[]>(report.Log);

        Assert.False(string.IsNullOrEmpty(report.StageError));
        Assert.Contains(log, entry => entry.Module == $"/{id}/Ok.lib.module.js" && entry.Hook == "beforeStart");
        Assert.DoesNotContain(log, entry => entry.Hook == "afterStarted");
        Assert.Contains(session.Requests, request => request.Path == $"/{id}/Missing.lib.module.js");
    }

    [Fact]
    public async Task Missing_module_response_matches_local_static_content()
    {
        var id = Guid.NewGuid();
        var files = Page([]);
        await using var session = await ContractSession.StartAsync([new ClientFiles(id, files)], useDefaultCacheProvider: true);
        using var response = await session.GetAsync($"/{id}/Missing.lib.module.js");
        var body = await response.Content.ReadAsStringAsync();
        var type = response.Content.Headers.ContentType?.MediaType ?? "";
        var cache = response.Headers.TryGetValues("Cache-Control", out var values) ? string.Join(", ", values) : "";

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("text/plain", type);
        Assert.Equal("There is no content at Missing.lib.module.js", body);
        // Kestrel's typed Cache-Control header rewrites directive order. Membership is exact.
        Assert.Equal(CacheDirectives(RemoteFilesMiddleware.MauiDefaultCacheControl), CacheDirectives(cache));
    }

    [HeadlessChromeFact]
    public async Task Second_client_gets_its_own_manifest_and_initializer()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        await using var session = await ContractSession.StartAsync(
        [
            new ClientFiles(first, Page(["A.lib.module.js"], ("A.lib.module.js", RecordModule))),
            new ClientFiles(second, Page(["B.lib.module.js"], ("B.lib.module.js", RecordModule)))
        ]);

        var firstReport = await session.LoadAsync(first, "");
        var marked = session.Requests.Count;
        var secondReport = await session.LoadAsync(second, "");
        var duringSecond = session.Requests.Skip(marked).ToArray();

        Assert.Equal(["A.lib.module.js"], Assert.IsType<string[]>(firstReport.Names));
        AssertOnce(firstReport, $"/{first}/A.lib.module.js");
        Assert.Equal(["B.lib.module.js"], Assert.IsType<string[]>(secondReport.Names));
        AssertOnce(secondReport, $"/{second}/B.lib.module.js");
        Assert.DoesNotContain(secondReport.Log!, entry => entry.Module.Contains(first.ToString(), StringComparison.Ordinal));
        Assert.DoesNotContain(duringSecond, request => request.Path.Contains(first.ToString(), StringComparison.Ordinal));
        Assert.Contains(duringSecond, request => request.Path == $"/{second}/_framework/blazor.modules.json");
        Assert.Contains(duringSecond, request => request.Path == $"/{second}/B.lib.module.js");
    }

    [HeadlessChromeFact]
    public async Task Refresh_runs_hooks_once_per_page_load()
    {
        var id = Guid.NewGuid();
        var files = Page(["Record.lib.module.js"], ("Record.lib.module.js", RecordModule));
        await using var session = await ContractSession.StartAsync([new ClientFiles(id, files)]);
        var reports = await session.LoadManyAsync(id, "?reload=1", 2);

        Assert.Equal(["1", "2"], reports.Select(report => report.Load ?? "").ToArray());
        Assert.All(reports, report =>
        {
            Assert.True(string.IsNullOrEmpty(report.StageError), report.StageError);
            AssertOnce(report, $"/{id}/Record.lib.module.js");
        });
        Assert.Equal(2, session.Requests.Count(request => request.Path == $"/{id}/Record.lib.module.js"));
    }

    [HeadlessChromeFact]
    public async Task Reconnect_runs_hooks_once_on_the_new_page_load()
    {
        var id = Guid.NewGuid();
        var files = Page(["Record.lib.module.js"], ("Record.lib.module.js", RecordModule));
        await using var session = await ContractSession.StartAsync([new ClientFiles(id, files)]);
        var first = await session.LoadAsync(id, "");
        AssertOnce(first, $"/{id}/Record.lib.module.js");

        await session.ReconnectAsync(id, files);
        var second = await session.LoadAsync(id, "");
        Assert.True(string.IsNullOrEmpty(second.StageError), second.StageError);
        AssertOnce(second, $"/{id}/Record.lib.module.js");
        Assert.Equal("1", second.Load);
    }

    [HeadlessChromeFact]
    public async Task Changed_initializer_has_a_new_hash_and_304_requires_the_current_etag()
    {
        var id = Guid.NewGuid();
        var moduleName = "Record.lib.module.js";
        var files = Page([moduleName], (moduleName, Marked("v1")));
        await using var session = await ContractSession.StartAsync([new ClientFiles(id, files)], useClientCache: true, useDefaultCacheProvider: true);
        string? v1Etag = null;
        var reports = await session.LoadManyAsync(id, "?swap=1", 2, async first =>
        {
            Assert.Equal("max-age=0", session.Requests.First(request => request.Path == $"/{id}/_framework/blazor.modules.json").CacheControl);
            Assert.Equal("", session.Requests.First(request => request.Path == $"/{id}/{HookName}").CacheControl);
            Assert.Equal("v1", first.Log!.Single(entry => entry.Hook == "beforeStart").Marker);
            var v1 = await ReadModule(session, id, moduleName);
            v1Etag = v1.ETag;
            Assert.Equal(Hash(Marked("v1")), v1.Hash);
            // No desktop cache provider on this line. A cached 200 uses the middleware's fixed header.
            Assert.Equal(CacheDirectives("public,max-age=3600"), CacheDirectives(v1.CacheControl));
            await AssertStatus(session, id, moduleName, v1.ETag, HttpStatusCode.NotModified, "");
            await AssertStatus(session, id, moduleName, "\"not-the-etag\"", HttpStatusCode.OK, v1.Hash);

            files[moduleName] = Marked("v2");
            files["module-init-swap.txt"] = "go"u8.ToArray();
        });

        Assert.Equal("v1", reports[0].Log!.Single(entry => entry.Hook == "beforeStart").Marker);
        // The module response is public,max-age=3600, so the reloaded page keeps v1.
        Assert.Equal("v1", reports[1].Log!.Single(entry => entry.Hook == "beforeStart").Marker);
        await AssertStatus(session, id, moduleName, v1Etag!, HttpStatusCode.OK, Hash(Marked("v2")));
        var v2 = await ReadModule(session, id, moduleName);
        Assert.Equal(Hash(Marked("v2")), v2.Hash);
        Assert.NotEqual(Hash(Marked("v1")), v2.Hash);
        await AssertStatus(session, id, moduleName, v2.ETag, HttpStatusCode.NotModified, "");
    }

    [HeadlessChromeFact]
    public async Task Empty_manifest_runs_no_hooks()
    {
        var id = Guid.NewGuid();
        var files = Page([]);
        await using var session = await ContractSession.StartAsync([new ClientFiles(id, files)]);
        var report = await session.LoadAsync(id, "");

        Assert.True(string.IsNullOrEmpty(report.StageError), report.StageError);
        Assert.Empty(report.Names!);
        Assert.Empty(report.Log!);
        Assert.DoesNotContain(session.Requests, request => request.Path.EndsWith(".lib.module.js", StringComparison.Ordinal));
    }

    [HeadlessChromeFact]
    public async Task Nested_content_path_resolves_against_the_rewritten_base_href()
    {
        var id = Guid.NewGuid();
        const string name = "_content/Lib/Nested.lib.module.js";
        var files = Page([name], (name, RecordModule));
        await using var session = await ContractSession.StartAsync([new ClientFiles(id, files)]);
        var report = await session.LoadAsync(id, "");

        Assert.True(string.IsNullOrEmpty(report.StageError), report.StageError);
        Assert.EndsWith($"/{id}/", report.BaseURI);
        Assert.Equal([name], Assert.IsType<string[]>(report.Names));
        AssertOnce(report, $"/{id}/{name}");
        Assert.Contains(session.Requests, request => request.Path == $"/{id}/{name}");
    }

    private static void AssertOnce(ContractReport report, string modulePath)
    {
        var log = Assert.IsType<LogEntry[]>(report.Log);
        Assert.Equal(1, log.Count(entry => entry.Module == modulePath && entry.Hook == "beforeStart" && entry.Phase != "leave"));
        Assert.Equal(1, log.Count(entry => entry.Module == modulePath && entry.Hook == "afterStarted"));
    }

    private static async Task<ModuleRead> ReadModule(ContractSession session, Guid id, string name)
    {
        using var response = await session.GetAsync($"/{id}/{name}");
        var body = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.TryGetValues("ETag", out var tags));
        var cache = response.Headers.TryGetValues("Cache-Control", out var cacheValues) ? string.Join(", ", cacheValues) : "";
        return new ModuleRead(Hash(body), tags!.Single(), cache);
    }

    private static async Task AssertStatus(ContractSession session, Guid id, string name, string etag, HttpStatusCode status, string hash)
    {
        using var response = await session.GetAsync($"/{id}/{name}", etag);
        Assert.Equal(status, response.StatusCode);
        var body = await response.Content.ReadAsByteArrayAsync();
        if (status == HttpStatusCode.NotModified)
            Assert.Empty(body);
        else
            Assert.Equal(hash, Hash(body));
    }

    private static Dictionary<string, byte[]> Page(string[] names, params (string Path, byte[] Bytes)[] modules)
    {
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["_framework/blazor.modules.json"] = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(names)),
            [HostPageName] = HostPage,
            [HookName] = HookScript
        };
        foreach (var (path, bytes) in modules)
            files[path] = bytes;
        return files;
    }

    private static byte[] Marked(string marker) => Encoding.UTF8.GetBytes(
        """
        export const marker = "MARKER";
        function push(hook, extra) {
          const log = (window.__moduleInitLog ||= []);
          log.push(Object.assign({ module: new URL(import.meta.url).pathname, hook, marker }, extra));
        }
        export function beforeStart() { push("beforeStart", { argc: arguments.length }); }
        export function afterStarted(blazor) {
          push("afterStarted", { argc: arguments.length, keys: blazor && typeof blazor === "object" ? Object.keys(blazor).sort() : [] });
        }
        """.Replace("MARKER", marker, StringComparison.Ordinal));

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    private static string CacheDirectives(string value) => string.Join(", ", value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).OrderBy(part => part, StringComparer.Ordinal));

    private static string Asset(string name) => Path.Combine(AppContext.BaseDirectory, "Assets", name);

    private sealed record ClientFiles(Guid Id, Dictionary<string, byte[]> Files);
    private sealed record ContractReport(string BaseURI, string[]? Names, string? StageError, string? Load, LogEntry[]? Log);
    private sealed record LogEntry(string Module, string Hook, string? Marker, string? Phase, int Argc, string[]? Keys);
    private sealed record ModuleRead(string Hash, string ETag, string CacheControl);
    private sealed record ObservedRequest(string Path, string Query, string CacheControl);

    private sealed partial class ContractSession : IAsyncDisposable
    {
        private readonly WebApplication _app;
        private readonly GrpcChannel _channel;
        private readonly ConcurrentDictionary<string, TaskCompletionSource<ServiceState>> _states;
        private readonly List<ClientSync> _clients = [];
        private readonly HttpClient _http = new();
        public ConcurrentQueue<ObservedRequest> Requests { get; }
        public string BrowserUrl { get; }

        private ContractSession(WebApplication app, GrpcChannel channel, ConcurrentDictionary<string, TaskCompletionSource<ServiceState>> states, string browserUrl, ConcurrentQueue<ObservedRequest> requests)
        {
            _app = app;
            _channel = channel;
            _states = states;
            BrowserUrl = browserUrl;
            Requests = requests;
        }

        public static async Task<ContractSession> StartAsync(IReadOnlyList<ClientFiles> clients, bool useClientCache = false, bool useDefaultCacheProvider = false)
        {
            AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);
            var requests = new ConcurrentQueue<ObservedRequest>();
            var states = new ConcurrentDictionary<string, TaskCompletionSource<ServiceState>>();
            var ready = new List<Task>();
            foreach (var client in clients)
                ready.Add(Seed(states, client.Id));

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
            builder.Services.AddSingleton(new RemoteFilesOptions { UseClientCache = useClientCache, UseServerCache = false });
            builder.Services.AddSingleton<ServerFileSyncManager>();
            builder.Services.AddSingleton<RemoteFileResolver>();
            builder.Services.AddSingleton<ShutdownService>();
            builder.Services.AddSingleton<RemoteWebViewService>();
            var app = builder.Build();
            app.Use(async (context, next) =>
            {
                requests.Enqueue(new ObservedRequest(
                    context.Request.Path.Value ?? string.Empty,
                    context.Request.QueryString.Value ?? string.Empty,
                    context.Request.Headers.CacheControl.ToString()));
                await next();
            });
            app.UseMiddleware<RemoteFilesMiddleware>();
            app.MapGrpcService<RemoteWebViewService>();
            await app.StartAsync();

            var session = new ContractSession(app, GrpcChannel.ForAddress($"http://127.0.0.1:{grpcPort}", new GrpcChannelOptions
            {
                HttpHandler = new SocketsHttpHandler { EnableMultipleHttp2Connections = true, UseCookies = false }
            }), states, $"http://127.0.0.1:{browserPort}", requests);
            foreach (var client in clients)
                session.Connect(client.Id, client.Files, useDefaultCacheProvider);
            await Task.WhenAll(ready).WaitAsync(TimeSpan.FromSeconds(15));
            return session;
        }

        public async Task<ContractReport> LoadAsync(Guid id, string query)
        {
            var reports = await LoadManyAsync(id, query, 1, null);
            return reports[0];
        }

        public async Task<ContractReport[]> LoadManyAsync(Guid id, string query, int reports, Func<ContractReport, Task>? afterFirst = null)
        {
            var chrome = ChromeSession.FindBrowser()
                ?? throw new InvalidOperationException("Google Chrome or Chromium was not found.");
            var seen = Reports().Count;
            var process = StartChrome(chrome, $"{BrowserUrl}/{id}/{HostPageName}{query}");
            try
            {
                if (afterFirst is not null)
                {
                    var first = await WaitForReports(seen, 1);
                    await afterFirst(first[0]);
                }
                return await WaitForReports(seen, reports);
            }
            finally
            {
                Kill(process);
            }
        }

        public async Task ReconnectAsync(Guid id, Dictionary<string, byte[]> files)
        {
            var client = _clients[0];
            await client.CloseAsync();
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (_states.ContainsKey(id.ToString()) && DateTime.UtcNow < deadline)
                await Task.Delay(20);
            Assert.False(_states.ContainsKey(id.ToString()));
            var ready = Seed(_states, id);
            Connect(id, files, useDefaultCacheProvider: false);
            await ready.WaitAsync(TimeSpan.FromSeconds(15));
        }

        public Task<HttpResponseMessage> GetAsync(string path, string? ifNoneMatch = null)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, BrowserUrl + path);
            if (ifNoneMatch is not null)
                request.Headers.TryAddWithoutValidation("If-None-Match", ifNoneMatch);
            return _http.SendAsync(request);
        }

        public async ValueTask DisposeAsync()
        {
            foreach (var client in _clients)
            {
                try
                {
                    await client.CloseAsync();
                }
                catch (InvalidOperationException)
                {
                }
            }
            _channel.Dispose();
            _http.Dispose();
            await _app.StopAsync();
            await _app.DisposeAsync();
        }

        private void Connect(Guid id, Dictionary<string, byte[]> files, bool useDefaultCacheProvider)
        {
            var ipc = new client::PeakSWC.RemoteWebView.WebViewIPC.WebViewIPCClient(_channel);
            var sync = new ClientSync(ipc, id, HostPageName, new MemoryFileProvider(files), static _ => { }, NullLogger.Instance);
            sync.HandleServerRequests(CancellationToken.None);
            _clients.Add(sync);
        }

        private static Task<bool> Seed(ConcurrentDictionary<string, TaskCompletionSource<ServiceState>> states, Guid id)
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
            states[id.ToString()] = source;
            return ready.Task;
        }

        private async Task<ContractReport[]> WaitForReports(int seen, int count)
        {
            var deadline = DateTime.UtcNow.AddSeconds(15);
            while (DateTime.UtcNow < deadline && Reports().Count < seen + count)
                await Task.Delay(20);
            var reports = Reports().Skip(seen).Take(count).Select(json => JsonSerializer.Deserialize<ContractReport>(json, JsonOptions)).ToArray();
            Assert.Equal(count, reports.Length);
            Assert.All(reports, report => Assert.NotNull(report));
            return reports!;
        }

        [GeneratedRegex(@"module-init-result\?report=([^&]*)")]
        private static partial Regex ModuleInitResultPattern();

        private List<string> Reports() => Requests
            .Select(request => ModuleInitResultPattern().Match(request.Path + request.Query))
            .Where(match => match.Success)
            .Select(match => Uri.UnescapeDataString(match.Groups[1].Value))
            .ToList();

        private static Process StartChrome(string chrome, string url)
        {
            var profile = Path.Combine(Path.GetTempPath(), "module-init-contract-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(profile);
            var process = new Process();
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
            process.StartInfo.ArgumentList.Add("--no-first-run");
            process.StartInfo.ArgumentList.Add("--disable-background-networking");
            process.StartInfo.ArgumentList.Add("--user-data-dir=" + profile);
            process.StartInfo.ArgumentList.Add(url);
            process.Start();
            ChromeSession.StartPipeReader(process.StandardOutput, new StringBuilder());
            ChromeSession.StartPipeReader(process.StandardError, new StringBuilder());
            return process;
        }

        private static void Kill(Process process)
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
            process.Dispose();
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
