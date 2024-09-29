//$(UserProfile)\.nuget\packages\$(AssemblyName.toLower())\$(Version)\lib
using Google.Protobuf;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipelines;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace PeakSWC.RemoteWebView
{
    public class RemoteWebView
    {
        public static IFileProvider CreateFileProvider(string contentRootDir, string hostPage, string manifestRoot = "embedded")
        {
            var root = Path.GetDirectoryName(hostPage) ?? string.Empty;
            var entryAssembly = Assembly.GetEntryAssembly()!;

            try
            {
                return new PhysicalFileProvider(contentRootDir);
            }
            catch (Exception) { }

            try
            {
                return new ManifestEmbeddedFileProvider(new FixedManifestEmbeddedAssembly(entryAssembly), Path.Combine(manifestRoot, root));
            }
            catch (Exception) { }

            try
            {
                return new EmbeddedFileProvider(entryAssembly);
            }
            catch (Exception) { }

            return new NullFileProvider();

        }
        public static void Restart(IBlazorWebView blazorWebView)
        {
            var psi = new ProcessStartInfo
            {
                FileName = Process.GetCurrentProcess().MainModule?.FileName
            };
            psi.ArgumentList.Add($"-u={blazorWebView.ServerUri}");
            psi.ArgumentList.Add($"-i={blazorWebView.Id}");
            Process p = new()
            {
                StartInfo = psi
            };
            p.Start();
        }

        private ILogger Logger { get; set; }

        #region private

        private IBlazorWebView BlazorWebView { get; init; }
        private readonly object bootLock = new();
        private WebViewIPC.WebViewIPCClient? client = null;
        private readonly CancellationTokenSource cts = new();
        private IFileProvider FileProvider { get; }
        #endregion

        public string HostHtmlPath { get; } = string.Empty;
        public Dispatcher? Dispatcher { get; set; }

        private uint PingIntervalSeconds {get;set;}

        public static async Task<Uri?> GetGrpcBaseUriAsync(Uri? serverUri)
        {
            Uri? _grpcBaseUri;
   
            try
            {
                var handler = new HttpClientHandler
                {
                    AutomaticDecompression = DecompressionMethods.GZip |
                                DecompressionMethods.Deflate |
                                DecompressionMethods.Brotli
                };

                var _httpClient = new HttpClient(handler);
                var jsonResponse = await _httpClient.GetStringAsync($"{serverUri}grpcbaseuri");

                // TODO wtf does this happen?
                jsonResponse = jsonResponse.Replace("://:", "https://localhost:");

                var json = JObject.Parse(jsonResponse);
                if (json.TryGetValue("grpcBaseUri", out JToken? grpcBaseUriToken))
                {
                    string grpcBaseUri = grpcBaseUriToken?.ToString() ?? string.Empty;

                    if (Uri.TryCreate(grpcBaseUri, UriKind.Absolute, out Uri? parsedUri))
                    {
                        _grpcBaseUri = parsedUri;
                    }
                    else
                    {
                        // Handle invalid URI
                        _grpcBaseUri = serverUri; // Or some other fallback logic
                    }
                }

                else
                {
                    _grpcBaseUri = serverUri;
                }
            }
            catch (Exception)
            {
                _grpcBaseUri = serverUri;
            }

            return _grpcBaseUri;
           
        }

        private GrpcChannel? channel;
        
        protected WebViewIPC.WebViewIPCClient? Client()
        {
            if (BlazorWebView.ServerUri == null) return null;
            if (BlazorWebView.GrpcBaseUri == null) return null;
            PingIntervalSeconds = BlazorWebView.PingIntervalSeconds;

            if (client == null)
            {
                channel = GrpcChannel.ForAddress(BlazorWebView.GrpcBaseUri,
                    new GrpcChannelOptions
                    {
                        HttpHandler = new SocketsHttpHandler
                        {
                            PooledConnectionIdleTimeout = Timeout.InfiniteTimeSpan,
                            KeepAlivePingDelay = TimeSpan.FromSeconds(110),
                            KeepAlivePingTimeout = TimeSpan.FromSeconds(100),   // 30 seconds is not enough to pass stress tests
                            EnableMultipleHttp2Connections = true
                        }
                    });

                client = new WebViewIPC.WebViewIPCClient(channel);
              
                Logger.LogInformation(" Id: {Id} ServerUri: {ServerUri} GrpcBaseUri: {GrpcBaseUri} Markup: {Markup} PingInterval: {PingIntervalSeconds} Group:{Group} EnableMirrors:{EnableMirrors}", BlazorWebView.Id, BlazorWebView.ServerUri, BlazorWebView.GrpcBaseUri, BlazorWebView.Markup.Replace("\r\n", "").Replace(" ", ""), PingIntervalSeconds, this.BlazorWebView.Group, this.BlazorWebView.EnableMirrors);
                var events = client.CreateWebView(new CreateWebViewRequest { Id = BlazorWebView.Id.ToString(), HtmlHostPath = HostHtmlPath, Markup = BlazorWebView.Markup, Group = BlazorWebView.Group, HostName = Dns.GetHostName(), Pid = Environment.ProcessId, ProcessName = Process.GetCurrentProcess().ProcessName, EnableMirrors = BlazorWebView.EnableMirrors }, cancellationToken: cts.Token);

                Exception? exception = ProcessBrowserMessages(BlazorWebView,events);

                if (exception != null)
                {
                    FireDisconnected(exception);
                    throw exception;
                }

                FileReader.AttachFileReader(client.RequestClientFileRead(), cts.Token, BlazorWebView.Id.ToString(), FileProvider, FireDisconnected, Logger);

                MonitorPingTask(BlazorWebView,client);

            }
            return client;
        }

        private void MonitorPingTask(IBlazorWebView blazorWebView, WebViewIPC.WebViewIPCClient client)
        {
            _ = Task.Factory.StartNew(async () =>
            {
                var pings = client.Ping();

                try
                {
                    await pings.RequestStream.WriteAsync(new PingMessageRequest { Id = blazorWebView.Id.ToString(), Initialize = true, PingIntervalSeconds = (int)PingIntervalSeconds },cts.Token).ConfigureAwait(false);

                    await foreach (var message in pings.ResponseStream.ReadAllAsync(cts.Token).ConfigureAwait(false))
                    {
                        if (message.Cancelled) throw new Exception("Ping timeout exceeded");

                        await pings.RequestStream.WriteAsync(new PingMessageRequest { Id = message.Id, Initialize = false }).ConfigureAwait(false);
                    }
                }
                catch (Exception ex)
                {
                    FireDisconnected(ex);
                }

            }, cts.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }

        private Exception? ProcessBrowserMessages(IBlazorWebView blazorWebView, AsyncServerStreamingCall<WebMessageResponse> events)
        {
            Exception? exception = null;
            bool connected = false;
            var completed = new ManualResetEventSlim(false);

            _ = Task.Factory.StartNew(async () =>
            {
                try
                {
                    await foreach (var message in events.ResponseStream.ReadAllAsync(cts.Token).ConfigureAwait(false))
                    {
                        ReadOnlySpan<char> responseSpan = message.Response.AsSpan();
                        int separatorIndex = responseSpan.IndexOf(':');

                        if (separatorIndex <= 0)
                        {
                            throw new Exception($"Invalid message format received: {message.Response}");
                        }

                        ReadOnlySpan<char> commandSpan = responseSpan[..separatorIndex];

                        try
                        {
                            if (commandSpan.Equals("__bwv", StringComparison.OrdinalIgnoreCase))
                            {
                                OnWebMessageReceived?.Invoke(message.Url, message.Response);
                            }
                            else if (commandSpan.Equals("browserAttached", StringComparison.OrdinalIgnoreCase))
                            {
                                _ = Task.Run(async () =>
                                {
                                    // TODO Create property for timeout value
                                    await Task.Delay(TimeSpan.FromSeconds(90),cts.Token).ConfigureAwait(false);

                                    if (!connected)
                                    {
                                        FireDisconnected(new Exception("Browser connection timed out"));
                                        cts.Cancel();
                                    }
                                },cts.Token);
                                completed.Set();
                            }
                            else if (commandSpan.Equals("created", StringComparison.OrdinalIgnoreCase))
                            {
                                completed.Set();
                                await blazorWebView.WaitForInitializationComplete().ConfigureAwait(false);
                                FireReadyToConnect();
                            }
                            else if (commandSpan.Equals("createFailed", StringComparison.OrdinalIgnoreCase))
                            {
                                exception = new Exception("WebView Create failed - Id must be unique");
                                Logger.LogError(exception, "WebView creation failed due to duplicate Id.");
                                completed.Set();
                                break; // Exit processing on failure
                            }
                            
                            else if (commandSpan.Equals("refreshed", StringComparison.OrdinalIgnoreCase))
                            {
                                lock (bootLock)
                                {
                                    Shutdown();
                                    FireRefreshed();
                                    Logger.LogInformation("Service refreshed. Connection shut down.");
                                    cts.Cancel();
                                }
                                break; // Exit processing after refresh
                            }
                            else if (commandSpan.Equals("shutdown", StringComparison.OrdinalIgnoreCase))
                            {
                                var shutdownException = new Exception("Server shut down connection");
                                FireDisconnected(shutdownException);
                                Logger.LogWarning(shutdownException, "Received 'shutdown' command from server.");
                                cts.Cancel();
                                break; // Exit processing on shutdown
                            }
                            else if (commandSpan.Equals("connected", StringComparison.OrdinalIgnoreCase))
                            {
                                ReadOnlySpan<char> payloadSpan = responseSpan[(separatorIndex + 1)..];
                                string payloadString = payloadSpan.ToString(); // Convert span to string here
                                connected = await HandleConnectedAsync(blazorWebView, payloadString, message.Cookies, cts.Token).ConfigureAwait(false);
                            }
                            else
                            {
                                throw new Exception($"Unknown command received: {commandSpan}");
                            }
                        }
                        catch (Exception ex)
                        {
                            exception = ex;
                            await ShutdownChannelAsync().ConfigureAwait(false);
                            completed.Set();
                            break; // Exit processing on exception
                        }
                    }
                }
                catch (OperationCanceledException ex) when (!connected)
                {
                    var timeoutException = new TimeoutException("Browser connection timed out.", ex);
                    FireDisconnected(timeoutException);
                    Logger.LogWarning(timeoutException, "Browser connection timed out.");
                    exception = timeoutException;
                }
                catch (Exception ex)
                {
                    exception = ex;
                    Logger.LogError(ex, "An unexpected error occurred while processing browser messages.");
                    await ShutdownChannelAsync().ConfigureAwait(false);
                }
                finally
                {
                    completed.Set();
                }
            }, TaskCreationOptions.LongRunning); 

            // Wait for the processing task to complete
            completed.Wait();

            return exception;
        }

        #region Helper Methods

        /// <summary>
        /// Handles the "connected" command by setting cookies and firing connected event.
        /// </summary>
        /// <param name="blazorWebView">The Blazor web view instance.</param>
        /// <param name="payload">The payload containing connection details.</param>
        /// <param name="cancellationToken">Token to observe for cancellation.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        private async Task<bool> HandleConnectedAsync(IBlazorWebView blazorWebView, string payload, string cookies, CancellationToken cancellationToken)
        {
            var connected = true;
            try
            {
                // Deserialize cookies from the cookies
                IDictionary<string, string>? cookiesDictionary = JsonConvert.DeserializeObject<IDictionary<string, string>>(cookies);
                if (cookiesDictionary != null)
                {
                    // Assuming Dispatcher is a UI thread dispatcher
                    if (Dispatcher != null)
                    {
                        await Dispatcher.InvokeAsync(() =>
                        {
                            try
                            {
                                foreach (var (name, value) in cookiesDictionary)
                                {
                                    var cookie = blazorWebView.CookieManager.CreateCookie(name, value, blazorWebView.ServerUri?.Host, "/");
                                    // Uncomment the line below if you intend to add/update cookies
                                    // blazorWebView.CookieManager.AddOrUpdateCookie(cookie);
                                }
                            }
                            catch (Exception ex)
                            {
                                Logger.LogError(ex, "Error setting cookies in Blazor WebView.");
                                connected = false;
                            }
                        });
                    }
                    else
                    {
                        Logger.LogError("Dispatcher is null. Cannot set cookies on the UI thread.");
                        connected = false;
                    }
                }
            }
            catch (Newtonsoft.Json.JsonException ex)
            {
                Logger.LogError(ex, "Failed to deserialize cookies from 'connected' message.");
                connected = false;
            }

            try
            {
                // Parse the payload: "connected:url|user"
                var split = payload.Split('|');
                var user = split.Length == 2 ? split[1] : string.Empty;
                var ipPart = split.Length > 0 ? split[0].Substring(split[0].IndexOf(':') + 1).Replace(":", "") : string.Empty;
                FireConnected(ipPart, user);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error parsing 'connected' message payload.");
                connected = false;
            }
            return connected;
        }



        /// <summary>
        /// Shuts down the communication channel gracefully.
        /// </summary>
        private async Task ShutdownChannelAsync()
        {
            cts.Cancel();
            if (channel != null)
            {
                try
                {
                    await channel.ShutdownAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "Error shutting down channel.");
                }
            }
        }

        
        #endregion

        void Shutdown()
        {
            Dispatcher?.InvokeAsync(() => Client()?.Shutdown(new IdMessageRequest { Id = BlazorWebView.Id.ToString() }));
        }

        void FireReadyToConnect()
        {
            Dispatcher?.InvokeAsync(() => BlazorWebView.FireReadyToConnect(new ReadyToConnectEventArgs(BlazorWebView.Id, BlazorWebView.ServerUri! )));
        }

        void FireConnected(string ip, string user)
        {
            Dispatcher?.InvokeAsync(() => BlazorWebView.FireConnected(new ConnectedEventArgs(BlazorWebView.Id, BlazorWebView.ServerUri!, ip, user)));
        }

        void FireDisconnected(Exception exception)
        {
            Dispatcher?.InvokeAsync(() => BlazorWebView.FireDisconnected(new DisconnectedEventArgs(BlazorWebView.Id, BlazorWebView.ServerUri!, exception)));
        }

        void FireRefreshed()
        {
            Dispatcher?.InvokeAsync(() => BlazorWebView.FireRefreshed(new RefreshedEventArgs(BlazorWebView.Id, BlazorWebView.ServerUri!)));
        }

        public event EventHandler<string>? OnWebMessageReceived;

        public static string GenMarkup(Uri? uri,Guid id)
        {
            var color = "#f1f1f1";
            var hostname = Dns.GetHostName();
      
            var url = $"{uri}app/{id}" ?? "";

            string style = $@"
            <style>
            .card{id} {{
                box-shadow: 0 4px 8px 0 rgba(0, 0, 0, 0.2);
                padding: 16px;
                text-align: center;
                background-color: {color};
                width: 300px;
                margin-bottom: 15px;
            }}
            </style>";
            string div = $@"
                {style}
                <div class='card{id}'>
                    <h3><a href = '{url}' > {hostname} </a></h3>
                </div>
                ";

            return div;
        }

        public RemoteWebView(IBlazorWebView blazorWebView,string hostHtmlPath, Dispatcher dispatcher, IFileProvider fileProvider, ILogger logger)
        {
            BlazorWebView = blazorWebView;
            HostHtmlPath = hostHtmlPath;
            Dispatcher = dispatcher;
            FileProvider = fileProvider;
            Logger = logger;

        }

        public void NavigateToUrl(string _url) { _ = Client(); }

        public void SendMessage(string message)
        {
            Client()?.SendMessage(new SendMessageRequest { Id = BlazorWebView.Id.ToString(), Message = message });
        }

        public void Initialize()
        {
            _ = Client();
        }
    }
}
