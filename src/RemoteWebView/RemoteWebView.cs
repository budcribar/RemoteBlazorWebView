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

            int i = 0;
            // Try to prevent COMException 0x8007139F
            while (p.MainWindowHandle == IntPtr.Zero)
            {
                // Refresh process property values
                p.Refresh();

                // Wait a bit before checking again
                Thread.Sleep(100);
                i++;
                if (i >= 100)
                    break;
            }
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

        protected WebViewIPC.WebViewIPCClient? Client()
        {
            if (BlazorWebView.ServerUri == null) return null;

            PingIntervalSeconds = BlazorWebView.PingIntervalSeconds;

            if (client == null)
            {
                var channel = GrpcChannel.ForAddress(BlazorWebView.GrpcBaseUri,
                    new GrpcChannelOptions
                    {
                        HttpHandler = new SocketsHttpHandler
                        {
                            PooledConnectionIdleTimeout = Timeout.InfiniteTimeSpan,
                            KeepAlivePingDelay = TimeSpan.FromSeconds(60),
                            KeepAlivePingTimeout = TimeSpan.FromSeconds(50),   // 30 seconds is not enough to pass stress tests
                            EnableMultipleHttp2Connections = true
                        }
                    });

                client = new WebViewIPC.WebViewIPCClient(channel);

                Logger.LogInformation(" Id: {Id} ServerUri: {ServerUri} GrpcBaseUri: {GrpcBaseUri} Markup: {Markup} PingInterval: {PingIntervalSeconds} Group:{Group} EnableMirrors:{EnableMirrors}", BlazorWebView.Id, BlazorWebView.ServerUri, BlazorWebView.GrpcBaseUri, BlazorWebView.Markup.Replace("\r\n", "").Replace(" ", ""), PingIntervalSeconds, this.BlazorWebView.Group, this.BlazorWebView.EnableMirrors  );
                var events = client.CreateWebView(new CreateWebViewRequest { Id = BlazorWebView.Id.ToString(), HtmlHostPath = HostHtmlPath, Markup = BlazorWebView.Markup, Group = BlazorWebView.Group, HostName = Dns.GetHostName(), Pid = Environment.ProcessId, ProcessName = Process.GetCurrentProcess().ProcessName, EnableMirrors = BlazorWebView.EnableMirrors }, cancellationToken: cts.Token);
                var completed = new ManualResetEventSlim();
                Exception? exception = null;

                _ = Task.Factory.StartNew(async () =>
                {
                    bool connected = false;
                    try
                    {
                        await foreach (var message in events.ResponseStream.ReadAllAsync())
                        {
                            var command = message.Response[..message.Response.IndexOf(':')];

                            try
                            {
                                switch (command)
                                {
                                    case "browserAttached":
                                        _ = Task.Run(async () =>
                                        {
                                            // TODO Create property for timeout value
                                            await Task.Delay(TimeSpan.FromSeconds(60));

                                            if (!connected)
                                            {
                                                FireDisconnected(new Exception("Browser connection timed out"));
                                                cts.Cancel();
                                            }
                                        });
                                        completed.Set();
                                        break;

                                    case "created":
                                        completed.Set();
                                        await BlazorWebView.WaitForInitializationComplete();
                                        FireReadyToConnect();
                                        break;

                                    case "createFailed":
                                        exception = new Exception("WebView Create failed - Id must be unique");
                                        completed.Set();
                                        break;

                                    case "__bwv":
                                        OnWebMessageReceived?.Invoke(message.Url, message.Response);
                                        break;

                                    case "refreshed":
                                        lock (bootLock)
                                        {
                                            Shutdown();
                                            FireRefreshed();
                                            cts.Cancel();
                                        }
                                        break;

                                    case "shutdown":
                                        FireDisconnected(new Exception("Server shut down connection"));
                                        cts.Cancel();
                                        break;

                                    case "connected":
                                        IDictionary<string, string>? cookiesDictionary = JsonConvert.DeserializeObject<IDictionary<string, string>>(message.Cookies);
                                        if (cookiesDictionary != null)
                                        {
                                            Dispatcher?.InvokeAsync(() =>
                                            {
                                                try
                                                {
                                                    // CookieManager could be null in Photino
                                                    foreach (var name in cookiesDictionary.Keys)
                                                    {
                                                        var cookie = BlazorWebView.CookieManager.CreateCookie(name, cookiesDictionary[name], BlazorWebView.ServerUri.Host, "/");
                                                        //BlazorWebView.CookieManager.AddOrUpdateCookie(cookie);
                                                    }
                                                }
                                                catch { }

                                            });


                                        }

                                        // connected:url|user
                                        try
                                        {
                                            var split = message.Response.Split("|");
                                            var user = split.Length == 2 ? split[1] : "";
                                            var ip = split[0].Substring(split[0].IndexOf(':') + 1).Replace(":", "");
                                            FireConnected(ip, user);
                                        }
                                        catch { }

                                        connected = true;
                                        break;
                                }
                            }
                            catch (Exception ex)
                            {
                                exception = ex;
                                completed.Set();
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        exception = ex;
                        completed.Set();
                    }
                }, cts.Token);

                completed.Wait();

                if (exception != null)
                {
                    FireDisconnected(exception);
                    throw exception;
                }

                _ = Task.Factory.StartNew(async () =>
                {
                    var files = client.FileReader();
                    try
                    {
                        await files.RequestStream.WriteAsync(new FileReadRequest { Id = BlazorWebView.Id.ToString(), Init = new() });

                        await foreach (var message in files.ResponseStream.ReadAllAsync(cts.Token))
                        {
                            try
                            {
                                var path = message.Path[(message.Path.IndexOf('/') + 1)..];

                                await files.RequestStream.WriteAsync(new FileReadRequest { Id = BlazorWebView.Id.ToString(), Length = new FileReadLengthRequest { Path = message.Path, Length = FileProvider.GetFileInfo(path).Length } });

                                using var stream = FileProvider.GetFileInfo(path).CreateReadStream() ?? null;
                                if (stream == null)
                                    await files.RequestStream.WriteAsync(new FileReadRequest { Id = BlazorWebView.Id.ToString(), Data = new FileReadDataRequest { Path = message.Path, Data = ByteString.Empty } });
                                else
                                {
                                    var buffer = new Byte[8 * 1024];
                                    int bytesRead = 0;

                                    while ((bytesRead = await stream.ReadAsync(buffer)) > 0)
                                    {
                                        ByteString bs = ByteString.CopyFrom(buffer, 0, bytesRead);
                                        await files.RequestStream.WriteAsync(new FileReadRequest { Id = BlazorWebView.Id.ToString(), Data = new FileReadDataRequest { Path = message.Path, Data = bs } });
                                    }
                                    await files.RequestStream.WriteAsync(new FileReadRequest { Id = BlazorWebView.Id.ToString(), Data = new FileReadDataRequest { Path = message.Path, Data = ByteString.Empty } });
                                }

                            }
                            catch (FileNotFoundException)
                            {
                                // TODO Warning to user?
                                await files.RequestStream.WriteAsync(new FileReadRequest { Id = BlazorWebView.Id.ToString(), Data = new FileReadDataRequest { Path = message.Path, Data = ByteString.Empty } });
                            }
                            catch (Exception ex)
                            {
                                FireDisconnected(ex);
                                await files.RequestStream.WriteAsync(new FileReadRequest { Id = BlazorWebView.Id.ToString(), Data = new FileReadDataRequest { Path = message.Path, Data = ByteString.Empty } });
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        FireDisconnected(ex);
                    }
                }, cts.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default);

                _ = Task.Factory.StartNew(async () =>
                {
                    var pings = client.Ping();

                    try
                    {
                        await pings.RequestStream.WriteAsync(new PingMessageRequest { Id = BlazorWebView.Id.ToString(), Initialize = true, PingIntervalSeconds = (int)PingIntervalSeconds });

                        await foreach (var message in pings.ResponseStream.ReadAllAsync(cts.Token))
                        {
                            if (message.Cancelled) throw new Exception("Ping timeout exceeded");

                            await pings.RequestStream.WriteAsync(new PingMessageRequest { Id = message.Id, Initialize = false });
                        }
                    }
                    catch (Exception ex)
                    {
                        FireDisconnected(ex);
                    }

                }, cts.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default);

            }
            return client;
        }
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
