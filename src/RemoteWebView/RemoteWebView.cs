//$(UserProfile)\.nuget\packages\$(AssemblyName.toLower())\$(Version)\lib
using Google.Protobuf;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace PeakSWC.RemoteWebView
{
    public class RemoteWebView 
    {
        public static IFileProvider CreateFileProvider(string contentRootDir, string hostPage, string manifestRoot = "embedded")
        {
            IFileProvider? provider = null;
            var root = Path.GetDirectoryName(hostPage) ?? string.Empty;
            var entryAssembly = Assembly.GetEntryAssembly()!;

            try
            {
                provider = new ManifestEmbeddedFileProvider(new FixedManifestEmbeddedAssembly(entryAssembly), Path.Combine(manifestRoot, root));
            }
            catch (Exception) { }
          
            if (provider == null)
                provider = new PhysicalFileProvider(contentRootDir);

            return provider;
        }
        public static void Restart(IBlazorWebView blazorWebView)
        {
            var psi = new ProcessStartInfo
            {
                FileName = Process.GetCurrentProcess().MainModule?.FileName
            };
            psi.ArgumentList.Add($"-u={blazorWebView.ServerUri}");
            psi.ArgumentList.Add($"-i={blazorWebView.Id}");

            Process.Start(psi);
        }


        #region private

        private IBlazorWebView BlazorWebView { get; init; }
        private readonly object bootLock = new();
        private WebViewIPC.WebViewIPCClient? client = null;
        private readonly CancellationTokenSource cts = new();
        private IFileProvider FileProvider { get; }
        #endregion

        public string HostHtmlPath { get; } = string.Empty;
        public Dispatcher? Dispacher { get; set; }

        protected WebViewIPC.WebViewIPCClient? Client
        {
            get
            {
                if (BlazorWebView.ServerUri == null) return null;

                if (client == null)
                {
                    var channel = GrpcChannel.ForAddress(BlazorWebView.ServerUri, 
                        new GrpcChannelOptions {
                        HttpHandler = new SocketsHttpHandler {
                        PooledConnectionIdleTimeout = Timeout.InfiniteTimeSpan,
                        KeepAlivePingDelay = TimeSpan.FromSeconds(60),
                        KeepAlivePingTimeout = TimeSpan.FromSeconds(50),   // 30 seconds is not enough to pass stress tests
                        EnableMultipleHttp2Connections = true
                    } });

                    client = new WebViewIPC.WebViewIPCClient(channel);
                   
                    var events = client.CreateWebView(new CreateWebViewRequest { Id = BlazorWebView.Id.ToString(), HtmlHostPath = HostHtmlPath, Markup = BlazorWebView.Markup, Group= BlazorWebView.Group, HostName = Dns.GetHostName(), Pid= Environment.ProcessId, ProcessName= Process.GetCurrentProcess().ProcessName, EnableMirrors=BlazorWebView.EnableMirrors}, cancellationToken: cts.Token); 
                    var completed = new ManualResetEventSlim();
                    Exception? exception = null;

                    Task.Factory.StartNew(async () =>
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
                                            await BlazorWebView.WaitForInitialitionComplete();
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
                                                Dispacher?.InvokeAsync(() =>
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
                                                var user = split[1];
                                                var ip = split[0].Substring(split[0].IndexOf(":") + 1);
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

                    Task.Factory.StartNew(async () =>
                    {
                        var files = client.FileReader();
                        try
                        {
                            await files.RequestStream.WriteAsync(new FileReadRequest {Id = BlazorWebView.Id.ToString(), Init = new () });

                            await foreach (var message in files.ResponseStream.ReadAllAsync(cts.Token))
                            {
                                try
                                {
                                    var path = message.Path[(message.Path.IndexOf("/") + 1)..];

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
                        } catch (Exception ex)
                        {
                            FireDisconnected(ex);
                        }
                    }, cts.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default);

                    Task.Factory.StartNew(async () => {
                        var pings = client.Ping();

                        try
                        {
                            await pings.RequestStream.WriteAsync(new PingMessageRequest { Id = BlazorWebView.Id.ToString(), Initialize = true, PingIntervalSeconds = 30 });

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


                void Shutdown()
                {
                    Dispacher?.InvokeAsync(() => Client?.Shutdown(new IdMessageRequest { Id = BlazorWebView.Id.ToString() }));
                }

                void FireReadyToConnect()
                {
                    Dispacher?.InvokeAsync(() => BlazorWebView.FireReadyToConnect(new ReadyToConnectEventArgs(BlazorWebView.Id, BlazorWebView.ServerUri)));
                }

                void FireConnected(string ip, string user)
                {
                    Dispacher?.InvokeAsync(() => BlazorWebView.FireConnected(new ConnectedEventArgs(BlazorWebView.Id, BlazorWebView.ServerUri, ip, user)));
                }

                void FireDisconnected(Exception exception)
                {
                    Dispacher?.InvokeAsync(() => BlazorWebView.FireDisconnected(new DisconnectedEventArgs(BlazorWebView.Id, BlazorWebView.ServerUri, exception)));
                }

                void FireRefreshed()
                {
                    Dispacher?.InvokeAsync(() => BlazorWebView.FireRefreshed(new RefreshedEventArgs(BlazorWebView.Id, BlazorWebView.ServerUri)));
                }
            }
        }

        public event EventHandler<string>? OnWebMessageReceived;

        private static string GenMarkup(Uri? uri,Guid id)
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

        public RemoteWebView(IBlazorWebView blazorWebView,string hostHtmlPath, Dispatcher dispatcher, IFileProvider fileProvider)
        {
            BlazorWebView = blazorWebView;
            HostHtmlPath = hostHtmlPath;
            Dispacher = dispatcher;
            FileProvider = fileProvider;
            BlazorWebView.Markup = string.IsNullOrWhiteSpace(BlazorWebView.Markup) ? GenMarkup(BlazorWebView.ServerUri, BlazorWebView.Id) : BlazorWebView.Markup;
            BlazorWebView.Group = string.IsNullOrWhiteSpace(BlazorWebView.Group) ? "test" : BlazorWebView.Group;
        }

        public void NavigateToUrl(string url) { _ = Client; }

        public void SendMessage(string message)
        {
            Client?.SendMessage(new SendMessageRequest { Id = BlazorWebView.Id.ToString(), Message = message });
        }

        public void Initialize()
        {
            _ = Client;
        }
    }
}
