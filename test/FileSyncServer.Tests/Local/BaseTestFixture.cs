// BaseTestFixture.cs
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Microsoft.Playwright;

namespace FileSyncServer.Tests.Local
{
    public abstract class BaseTestFixture : IAsyncLifetime
    {
        public IPlaywright PlaywrightInstance { get; private set; } = default!;
        public IBrowser Browser { get; private set; } = default!;
        public IPage Page { get; private set; } = default!;
        public Process? AppProcess { get; private set; }

        protected string ClientExecutablePath { get; set; } = string.Empty;

        // WebView2's first browser process on a hosted runner can take longer than
        // the old 15s poll, and a fixed port collides with a process leaked when
        // InitializeAsync throws (xUnit does not call DisposeAsync in that case).
        private readonly TimeSpan BrowserWsTimeout = TimeSpan.FromSeconds(60);
        private int _remoteDebuggingPort;
        private string? _userDataFolder;
        private DateTime _processStartedUtc;
        private readonly StringBuilder _stdout = new();
        private readonly StringBuilder _stderr = new();

        protected BaseTestFixture() { }

        public async Task InitializeAsync()
        {
            try
            {
                AppProcess = StartApplication();

                PlaywrightInstance = await Playwright.CreateAsync();

                var browserWsUrl = await GetBrowserWebSocketUrlAsync(_remoteDebuggingPort, BrowserWsTimeout);
                if (string.IsNullOrEmpty(browserWsUrl))
                {
                    throw new InvalidOperationException(DescribeFailure("WebView2 did not open a DevTools endpoint."));
                }

                Browser = await PlaywrightInstance.Chromium.ConnectOverCDPAsync(browserWsUrl);

                var contexts = Browser.Contexts.ToList();
                if (contexts.Count == 0)
                {
                    throw new InvalidOperationException(DescribeFailure("No browser contexts found in the connected WebView2 instance."));
                }

                var pages = contexts[0].Pages.ToList();
                var atConnect = DescribeTargets();
                if (pages.Count == 0)
                {
                    // Observation only. The test still fails. The poll records whether a page
                    // appears in any context after the first empty snapshot.
                    var poll = new StringBuilder();
                    string listText = await ReadJsonListAsync();

                    for (var i = 1; i <= 8; i++)
                    {
                        await Task.Delay(500);
                        try
                        {
                            poll.Append($"t+{i * 500}ms {DescribeTargets()};");
                            if (Browser.Contexts.Any(context => context.Pages.Count > 0))
                                break;
                        }
                        catch (Exception ex)
                        {
                            poll.Append($"t+{i * 500}ms={ex.GetType().Name};");
                            break;
                        }
                    }

                    var detail = "No pages found in the first browser context. atConnect=" + atConnect
                        + " pagePoll=" + poll + " jsonList=" + Tail(listText);
                    Note(detail);
                    throw new InvalidOperationException(DescribeFailure(detail));
                }

                Note($"pagesAtConnect context0={pages.Count} {atConnect} devtoolsMs={(DateTime.UtcNow - _processStartedUtc).TotalMilliseconds:0}");
                Page = pages[0];
            }
            catch
            {
                // xUnit does not call DisposeAsync when InitializeAsync throws.
                StopApp();
                throw;
            }
        }

        public async Task DisposeAsync()
        {
            try
            {
                if (Browser != null)
                    await Browser.CloseAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error closing Playwright browser: {ex.Message}");
            }

            try
            {
                PlaywrightInstance?.Dispose();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error disposing Playwright: {ex.Message}");
            }

            StopApp();
        }

        private Process StartApplication()
        {
            if (!File.Exists(ClientExecutablePath))
            {
                throw new FileNotFoundException($"Application executable not found at path: {ClientExecutablePath}", ClientExecutablePath);
            }

            // A previous test, or an earlier workflow step, can leave the tutorial
            // process and its WebView2 browser alive. A browser started without the
            // debugging port keeps the user-data folder and ignores the port later.
            StopProcessesNamed(Path.GetFileNameWithoutExtension(ClientExecutablePath));

            _remoteDebuggingPort = GetFreeLoopbackPort();
            _userDataFolder = Path.Combine(Path.GetTempPath(), "wv2-cdp", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_userDataFolder);

            var startInfo = new ProcessStartInfo
            {
                FileName = Path.GetFullPath(ClientExecutablePath),
                Arguments = "",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                CreateNoWindow = false,
                WorkingDirectory = Path.GetFullPath(Path.GetDirectoryName(ClientExecutablePath) ?? "")
            };

            // Chromium binds the DevTools port to 127.0.0.1. --remote-allow-origins
            // is required for Playwright's CDP websocket on current WebView2 runtimes.
            startInfo.Environment["WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS"] =
                $"--remote-debugging-port={_remoteDebuggingPort} --remote-allow-origins=*";
            startInfo.Environment["WEBVIEW2_USER_DATA_FOLDER"] = _userDataFolder;

            var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
            process.OutputDataReceived += (_, e) =>
            {
                if (string.IsNullOrEmpty(e.Data))
                    return;
                lock (_stdout)
                    _stdout.AppendLine(e.Data);
                Console.WriteLine($"[Output]: {e.Data}");
            };
            process.ErrorDataReceived += (_, e) =>
            {
                if (string.IsNullOrEmpty(e.Data))
                    return;
                lock (_stderr)
                    _stderr.AppendLine(e.Data);
                Console.Error.WriteLine($"[Error]: {e.Data}");
            };

            _processStartedUtc = DateTime.UtcNow;
            if (!process.Start())
            {
                throw new InvalidOperationException($"Failed to start {startInfo.FileName}.");
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            return process;
        }

        private async Task<string?> GetBrowserWebSocketUrlAsync(int port, TimeSpan timeout)
        {
            using var handler = new SocketsHttpHandler
            {
                UseProxy = false,
                ConnectTimeout = TimeSpan.FromSeconds(2)
            };
            using var httpClient = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(2) };
            // 127.0.0.1, not localhost: WebView2 listens on IPv4 and localhost can resolve to ::1.
            var url = $"http://127.0.0.1:{port}/json/version";
            var startTime = DateTime.UtcNow;

            while (DateTime.UtcNow - startTime < timeout)
            {
                if (AppProcess != null && AppProcess.HasExited)
                {
                    throw new InvalidOperationException(DescribeFailure($"Process exited with code {AppProcess.ExitCode} before the DevTools endpoint was ready."));
                }

                try
                {
                    var response = await httpClient.GetAsync(url);
                    if (response.IsSuccessStatusCode)
                    {
                        var json = await response.Content.ReadAsStringAsync();
                        using var doc = JsonDocument.Parse(json);
                        if (doc.RootElement.TryGetProperty("webSocketDebuggerUrl", out var wsUrlElement))
                        {
                            return wsUrlElement.GetString();
                        }
                    }
                }
                catch (InvalidOperationException)
                {
                    throw;
                }
                catch
                {
                    // The browser process has not bound the port yet.
                }

                // Repro branch only. 200ms steps can step over the gap between the
                // debugging port opening and the first page target. 20ms samples that gap.
                await Task.Delay(20);
            }

            return null;
        }

        private string DescribeTargets()
        {
            var contexts = Browser.Contexts.ToList();
            var pages = string.Join("/", contexts.Select(context => context.Pages.Count));
            return $"contexts={contexts.Count} pages={pages}";
        }

        private async Task<string> ReadJsonListAsync()
        {
            try
            {
                using var listClient = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
                var json = await listClient.GetStringAsync($"http://127.0.0.1:{_remoteDebuggingPort}/json/list");
                using var doc = JsonDocument.Parse(json);
                var rows = new List<string>();
                foreach (var target in doc.RootElement.EnumerateArray())
                {
                    var type = target.TryGetProperty("type", out var typeElement) ? typeElement.GetString() : "?";
                    var url = target.TryGetProperty("url", out var urlElement) ? urlElement.GetString() : "";
                    rows.Add(type + ":" + url);
                }
                return rows.Count == 0 ? "<no targets>" : string.Join(" | ", rows);
            }
            catch (Exception ex)
            {
                return ex.GetType().Name + ": " + ex.Message;
            }
        }

        private static void Note(string line)
        {
            var path = Environment.GetEnvironmentVariable("NOPAGES_LOG");
            if (string.IsNullOrEmpty(path))
                return;
            File.AppendAllText(path, DateTime.UtcNow.ToString("O") + " " + line + Environment.NewLine);
        }

        private string DescribeFailure(string reason)
        {
            var exited = AppProcess?.HasExited;
            int? exitCode = null;
            if (AppProcess is { HasExited: true })
            {
                try { exitCode = AppProcess.ExitCode; } catch { }
            }

            string stdout;
            string stderr;
            lock (_stdout)
                stdout = Tail(_stdout.ToString());
            lock (_stderr)
                stderr = Tail(_stderr.ToString());

            return $"{reason} exe={ClientExecutablePath} port={_remoteDebuggingPort} userData={_userDataFolder} exited={exited} exitCode={exitCode} stdout={stdout} stderr={stderr}";
        }

        private static string Tail(string text)
        {
            text = text.Trim();
            if (text.Length == 0)
                return "<empty>";
            return text.Length <= 1800 ? text : text[^1800..];
        }

        private void StopApp()
        {
            try
            {
                if (AppProcess is { HasExited: false })
                {
                    AppProcess.Kill(entireProcessTree: true);
                    AppProcess.WaitForExit(5000);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error stopping {ClientExecutablePath}: {ex.Message}");
            }

            if (!string.IsNullOrEmpty(_userDataFolder))
            {
                try { Directory.Delete(_userDataFolder, recursive: true); }
                catch { }
            }
        }

        private static void StopProcessesNamed(string processName)
        {
            foreach (var process in Process.GetProcessesByName(processName))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit(5000);
                }
                catch
                {
                    // The process may already have exited.
                }
            }
        }

        private static int GetFreeLoopbackPort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }
    }
}
