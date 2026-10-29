using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json.Nodes;

namespace StaticContentCache.EndToEnd.Tests;

/// <summary>
/// Headless Google Chrome (or Chromium) over the DevTools protocol.
/// Playwright is not referenced. <see cref="FindBrowser"/> is the one lookup
/// used to decide whether a browser test runs and which binary it starts:
/// CHROME_PATH, a known install, a PATH binary, or Playwright's Chrome for
/// Testing under ms-playwright/chromium-*. The headless shell is not used.
/// Each launch gets its own user-data directory so a wrapper that pins port
/// 9222 is not used.
/// </summary>
public sealed class ChromeSession : IAsyncDisposable
{
    private readonly Process _process;
    private readonly int _port;
    private readonly string _userData;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(5) };
    private readonly StringBuilder _error = new();

    private ChromeSession(Process process, int port, string userData)
    {
        _process = process;
        _port = port;
        _userData = userData;
    }

    public static string? FindBrowser()
    {
        var env = Environment.GetEnvironmentVariable("CHROME_PATH");
        if (IsUsableBrowser(env))
            return env;

        foreach (var candidate in InstalledCandidates())
        {
            if (IsUsableBrowser(candidate))
                return candidate;
        }

        var localAppData = Environment.GetEnvironmentVariable("LOCALAPPDATA");
        if (string.IsNullOrEmpty(localAppData))
            localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        foreach (var root in PlaywrightRegistryRoots(
            Environment.GetEnvironmentVariable("PLAYWRIGHT_BROWSERS_PATH"),
            localAppData,
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Environment.GetEnvironmentVariable("XDG_CACHE_HOME"),
            OperatingSystem.IsWindows()))
        {
            var playwright = FindPlaywrightChrome(root);
            if (playwright is not null)
                return playwright;
        }

        return null;
    }

    internal static IEnumerable<string> PlaywrightRegistryRoots(string? browsersPath, string? localAppData, string? home, string? xdgCacheHome, bool windows)
    {
        // Playwright treats "0" as "beside the package". This lookup does not
        // know that directory. An explicit path replaces the default registry.
        if (string.Equals(browsersPath, "0", StringComparison.Ordinal))
            yield break;
        if (!string.IsNullOrWhiteSpace(browsersPath))
        {
            yield return Path.IsPathRooted(browsersPath) ? browsersPath : Path.GetFullPath(browsersPath);
            yield break;
        }

        if (windows)
        {
            if (!string.IsNullOrEmpty(localAppData))
                yield return Path.Combine(localAppData, "ms-playwright");
            yield break;
        }

        if (!string.IsNullOrEmpty(xdgCacheHome))
            yield return Path.Combine(xdgCacheHome, "ms-playwright");
        if (!string.IsNullOrEmpty(home))
            yield return Path.Combine(home, ".cache", "ms-playwright");
    }

    internal static string? FindPlaywrightChrome(string registryRoot)
    {
        if (string.IsNullOrEmpty(registryRoot) || !Directory.Exists(registryRoot))
            return null;

        List<(int Revision, string Directory)> installs = [];
        IEnumerable<string> directories;
        try
        {
            directories = Directory.EnumerateDirectories(registryRoot);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        foreach (var directory in directories)
        {
            var name = Path.GetFileName(directory);
            const string prefix = "chromium-";
            if (!name.StartsWith(prefix, StringComparison.Ordinal))
                continue;
            if (!int.TryParse(name.AsSpan(prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var revision))
                continue;
            installs.Add((revision, directory));
        }

        foreach (var install in installs.OrderByDescending(install => install.Revision))
        {
            foreach (var relative in PlaywrightChromeRelativePaths)
            {
                var candidate = Path.Combine(install.Directory, relative);
                if (IsUsableBrowser(candidate))
                    return candidate;
            }
        }

        return null;
    }

    private static IEnumerable<string> InstalledCandidates()
    {
        yield return "/usr/bin/google-chrome-stable";
        yield return "/usr/bin/chromium";
        yield return "/usr/bin/chromium-browser";
        yield return "/opt/google/chrome/chrome";
        yield return @"C:\Program Files\Google\Chrome\Application\chrome.exe";
        yield return @"C:\Program Files (x86)\Google\Chrome\Application\chrome.exe";

        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(path))
            yield break;

        string[] names = OperatingSystem.IsWindows()
            ? ["chrome.exe", "chrome", "google-chrome.exe", "chromium.exe"]
            : ["google-chrome-stable", "google-chrome", "chromium", "chromium-browser", "chrome"];
        foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var name in names)
                yield return Path.Combine(dir, name);
        }
    }

    private static readonly string[] PlaywrightChromeRelativePaths =
    [
        Path.Combine("chrome-win64", "chrome.exe"),
        Path.Combine("chrome-win", "chrome.exe"),
        Path.Combine("chrome-linux64", "chrome"),
        Path.Combine("chrome-linux", "chrome"),
        Path.Combine("chrome-linux-arm64", "chrome")
    ];

    private static bool IsUsableBrowser(string? path)
    {
        if (string.IsNullOrEmpty(path))
            return false;
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists)
                return false;
            _ = info.Length;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }

        return !ForcesSharedDebugPort(path);
    }

    public static async Task<ChromeSession> StartAsync()
    {
        var binary = FindBrowser() ?? throw new InvalidOperationException("Google Chrome or Chromium was not found.");
        var port = FreePort();
        var userData = Path.Combine(Path.GetTempPath(), "module-initializer-chrome-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(userData);
        var start = new ProcessStartInfo(binary)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        start.ArgumentList.Add("--headless=new");
        start.ArgumentList.Add("--no-sandbox");
        start.ArgumentList.Add("--disable-gpu");
        start.ArgumentList.Add("--disable-dev-shm-usage");
        start.ArgumentList.Add("--no-first-run");
        start.ArgumentList.Add("--disable-background-networking");
        start.ArgumentList.Add("--remote-allow-origins=*");
        start.ArgumentList.Add($"--remote-debugging-port={port}");
        start.ArgumentList.Add($"--user-data-dir={userData}");
        start.ArgumentList.Add("about:blank");
        var process = Process.Start(start) ?? throw new InvalidOperationException("Chrome did not start.");
        var session = new ChromeSession(process, port, userData);
        session.Drain(process.StandardOutput);
        session.Drain(process.StandardError);
        await session.WaitForDebuggerAsync();
        return session;
    }

    public async Task<ChromePage> OpenAsync(string url)
    {
        using var created = await _http.PutAsync($"http://127.0.0.1:{_port}/json/new", new StringContent(""));
        created.EnsureSuccessStatusCode();
        var body = JsonNode.Parse(await created.Content.ReadAsStringAsync()) as JsonObject
            ?? throw new InvalidOperationException("Chrome did not return a page target.");
        var socketUrl = body["webSocketDebuggerUrl"]!.GetValue<string>();
        var targetId = body["id"]!.GetValue<string>();
        var socket = new ClientWebSocket();
        await socket.ConnectAsync(new Uri(socketUrl), CancellationToken.None);
        var page = new ChromePage(socket, targetId, _port);
        await page.StartReadingAsync();
        await page.SendAsync("Page.enable");
        await page.SendAsync("Network.enable");
        await page.SendAsync("Runtime.enable");
        await page.SendAsync("Page.navigate", new JsonObject { ["url"] = url });
        return page;
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (!_process.HasExited)
                _process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
        }

        _process.Dispose();
        _http.Dispose();
        try
        {
            if (Directory.Exists(_userData))
                Directory.Delete(_userData, recursive: true);
        }
        catch (IOException)
        {
        }

        await Task.CompletedTask;
    }

    private void Drain(StreamReader reader)
    {
        _ = Task.Run(async () =>
        {
            var buffer = new char[1024];
            while (true)
            {
                var read = await reader.ReadAsync(buffer);
                if (read == 0)
                    break;
                lock (_error)
                {
                    _error.Append(buffer, 0, read);
                    if (_error.Length > 4000)
                        _error.Remove(0, _error.Length - 4000);
                }
            }
        });
    }

    private Task WaitForDebuggerAsync()
        => WaitForHttpAsync(
            async () =>
            {
                using var response = await _http.GetAsync($"http://127.0.0.1:{_port}/json/version");
                return response.IsSuccessStatusCode;
            },
            () => _process.HasExited,
            TimeSpan.FromSeconds(15),
            () => "Chrome exited. " + _error,
            () => "Chrome DevTools did not accept connections. " + _error);

    // The 15s deadline is the wait. One probe uses HttpClient.Timeout (5s). A slow
    // DevTools accept must be retried inside that deadline, not fail the test early.
    internal static async Task WaitForHttpAsync(
        Func<Task<bool>> probe,
        Func<bool> hasExited,
        TimeSpan budget,
        Func<string> exitDetail,
        Func<string> timeoutDetail)
    {
        var deadline = DateTime.UtcNow.Add(budget);
        while (DateTime.UtcNow < deadline)
        {
            if (hasExited())
                throw new InvalidOperationException(exitDetail());
            try
            {
                if (await probe())
                    return;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
            }

            var remaining = deadline - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero)
                break;
            await Task.Delay(TimeSpan.FromMilliseconds(Math.Min(100, remaining.TotalMilliseconds)));
        }

        throw new TimeoutException(timeoutDetail());
    }

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static bool ForcesSharedDebugPort(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length > 64 * 1024)
                return false;
            return File.ReadAllText(path).Contains("remote-debugging-port=9222", StringComparison.Ordinal);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}

public sealed class ChromePage : IAsyncDisposable
{
    private const string SnapshotExpression = """
        (() => ({
          probe: window.__moduleInitializerProbe ? {
            bootError: window.__moduleInitializerProbe.bootError,
            unhandled: window.__moduleInitializerProbe.unhandled,
            ipc: window.__moduleInitializerProbe.ipc
          } : null,
          baseline: window.__moduleInitBaseline || null,
          order: window.__initializerOrder || null,
          args: window.__initializerArgs || null,
          nested: window.__nestedModule || null,
          changed: window.__changedModule || null,
          other: window.__otherModule || null,
          baseHref: document.querySelector("base") ? document.querySelector("base").href : null,
          reloading: window.__reloading === true
        }))()
        """;

    private readonly ClientWebSocket _socket;
    private readonly string _targetId;
    private readonly int _port;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(5) };
    private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonNode>> _pending = new();
    private readonly SemaphoreSlim _send = new(1, 1);
    private readonly CancellationTokenSource _stop = new();
    private readonly List<string> _requests = [];
    private readonly List<string> _console = [];
    private int _next;
    private Task? _reader;

    internal ChromePage(ClientWebSocket socket, string targetId, int port)
    {
        _socket = socket;
        _targetId = targetId;
        _port = port;
    }

    public IReadOnlyList<string> RequestUrls
    {
        get
        {
            lock (_requests)
                return _requests.ToArray();
        }
    }

    public string ConsoleText
    {
        get
        {
            lock (_console)
                return string.Join(Environment.NewLine, _console);
        }
    }

    internal async Task StartReadingAsync()
    {
        _reader = Task.Run(ReadLoopAsync);
        await Task.CompletedTask;
    }

    public async Task<JsonObject> SnapshotAsync()
    {
        var value = await EvaluateAsync(SnapshotExpression);
        return value as JsonObject ?? throw new InvalidOperationException("Page snapshot was not an object.");
    }

    public async Task<JsonNode?> EvaluateAsync(string expression)
    {
        var message = await SendAsync("Runtime.evaluate", new JsonObject
        {
            ["expression"] = expression,
            ["returnByValue"] = true,
            ["awaitPromise"] = true
        });
        var payload = message["result"] as JsonObject ?? throw new InvalidOperationException(message.ToJsonString());
        if (payload["exceptionDetails"] is JsonObject details)
            throw new InvalidOperationException(details["text"]?.ToString() ?? details.ToJsonString());
        var remote = payload["result"] as JsonObject ?? throw new InvalidOperationException(payload.ToJsonString());
        if (remote["type"]?.GetValue<string>() == "undefined")
            return null;
        return remote["value"];
    }

    public Task ReloadAsync()
        => SendAsync("Page.reload", new JsonObject { ["ignoreCache"] = true });

    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        try
        {
            if (_socket.State == WebSocketState.Open)
                await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None);
        }
        catch (WebSocketException)
        {
        }

        _socket.Dispose();
        try
        {
            await _http.GetAsync($"http://127.0.0.1:{_port}/json/close/{_targetId}");
        }
        catch (HttpRequestException)
        {
        }

        _http.Dispose();
        _send.Dispose();
        _stop.Dispose();
    }

    internal async Task<JsonObject> SendAsync(string method, JsonObject? parameters = null)
    {
        var id = Interlocked.Increment(ref _next);
        var message = new JsonObject { ["id"] = id, ["method"] = method };
        if (parameters is not null)
            message["params"] = parameters;
        var waiter = new TaskCompletionSource<JsonNode>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = waiter;
        var bytes = Encoding.UTF8.GetBytes(message.ToJsonString());
        await _send.WaitAsync();
        try
        {
            await _socket.SendAsync(bytes, WebSocketMessageType.Text, true, _stop.Token);
        }
        finally
        {
            _send.Release();
        }

        var completed = await Task.WhenAny(waiter.Task, Task.Delay(TimeSpan.FromSeconds(15), _stop.Token));
        if (completed != waiter.Task)
            throw new TimeoutException("DevTools command timed out: " + method);
        var node = await waiter.Task;
        if (node is not JsonObject obj)
            throw new InvalidOperationException(method + " returned " + node);
        if (obj["error"] is JsonObject error)
            throw new InvalidOperationException(method + " failed: " + error.ToJsonString());
        return obj;
    }

    private async Task ReadLoopAsync()
    {
        try
        {
            while (!_stop.IsCancellationRequested && _socket.State == WebSocketState.Open)
            {
                var text = await ReceiveTextAsync();
                var node = JsonNode.Parse(text);
                if (node is JsonObject obj
                    && obj["id"] is JsonValue idValue
                    && idValue.TryGetValue<int>(out var id)
                    && _pending.TryRemove(id, out var waiter))
                {
                    waiter.TrySetResult(node);
                    continue;
                }

                Capture(node);
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or WebSocketException or InvalidOperationException or ObjectDisposedException)
        {
            foreach (var waiter in _pending.Values)
                waiter.TrySetException(ex);
        }
    }

    private void Capture(JsonNode? node)
    {
        if (node is not JsonObject obj || obj["method"]?.GetValue<string>() is not string method)
            return;
        var parameters = obj["params"] as JsonObject;
        if (method == "Network.requestWillBeSent")
        {
            var url = parameters?["request"]?["url"]?.GetValue<string>();
            if (url is not null)
            {
                lock (_requests)
                    _requests.Add(url);
            }
        }
        else if (method == "Runtime.consoleAPICalled")
        {
            var type = parameters?["type"]?.GetValue<string>() ?? "log";
            var args = parameters?["args"] as JsonArray;
            var text = args is null
                ? ""
                : string.Join(" ", args.Select(arg => arg?["value"]?.ToJsonString() ?? arg?["description"]?.ToString() ?? ""));
            lock (_console)
            {
                if (_console.Count < 40)
                    _console.Add(type + ": " + text);
            }
        }
    }

    private async Task<string> ReceiveTextAsync()
    {
        using var message = new MemoryStream();
        var buffer = new byte[16 * 1024];
        while (true)
        {
            var result = await _socket.ReceiveAsync(buffer, _stop.Token);
            if (result.MessageType == WebSocketMessageType.Close)
                throw new WebSocketException("DevTools socket closed.");
            message.Write(buffer, 0, result.Count);
            if (result.EndOfMessage)
                break;
        }

        return Encoding.UTF8.GetString(message.ToArray());
    }
}

public sealed class HeadlessChromeFactAttribute : FactAttribute
{
    public const string Reason =
        "Requires Google Chrome or Chromium (CHROME_PATH or google-chrome-stable). " +
        "Does not require Windows WebView2: the page loads the real blazor.webview.js and a test-only window.external stub. " +
        "Skip on hosts that have neither Chrome nor Chromium; Windows WebView2 CI does not run this page unless Chrome is installed.";

    public HeadlessChromeFactAttribute()
    {
        if (ChromeSession.FindBrowser() is null)
            Skip = Reason;
    }
}
