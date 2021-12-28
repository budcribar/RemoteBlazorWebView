//$(UserProfile)\.nuget\packages\$(AssemblyName.toLower())\$(Version)\lib
using Google.Protobuf;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.FileProviders;
using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace PeakSWC.RemoteWebView
{
    public class RemoteWebView 
    {
        public static void Restart(IBlazorWebView blazorWebView)
        {
            var psi = new ProcessStartInfo
            {
                FileName = Process.GetCurrentProcess().MainModule?.FileName
            };
            psi.ArgumentList.Add($"-u={blazorWebView.ServerUri}");
            psi.ArgumentList.Add($"-i={blazorWebView.Id}");
            psi.ArgumentList.Add($"-r=true");

            Process.Start(psi);
        }

        public static Task<Process?> StartBrowser(IBlazorWebView blazorWebView)
        {
            var url = $"{blazorWebView.ServerUri}app/{blazorWebView.Id}";

            return Task.Run(() =>
            {
                Process? p = null;
                try
                {
                    p = Process.Start(new ProcessStartInfo("cmd", $"/c start " + url) { CreateNoWindow = true });
                }
                catch (Exception)
                {
                    p = null;
                }
                return p;
            });

        }

        #region private

        private string Markup { get; init; }
        private IBlazorWebView BlazorWebView { get; init; }
        private readonly object bootLock = new();
        private string Group { get; init; }
        private WebViewIPC.WebViewIPCClient? client = null;
        private readonly CancellationTokenSource cts = new();
        private IFileProvider FileProvider { get; }
        #endregion

        public Uri? ServerUri { get; }
        public string HostHtmlPath { get; } = string.Empty;
        public string Id { get; }
        public Dispatcher? Dispacher { get; set; }

        protected WebViewIPC.WebViewIPCClient? Client
        {
            get
            {
                if (ServerUri == null) return null;

                if (client == null)
                {
                    var channel = GrpcChannel.ForAddress(ServerUri, 
                        new GrpcChannelOptions {
                        HttpHandler = new SocketsHttpHandler {
                        PooledConnectionIdleTimeout = Timeout.InfiniteTimeSpan,
                        KeepAlivePingDelay = TimeSpan.FromSeconds(60),
                        KeepAlivePingTimeout = TimeSpan.FromSeconds(50),   // 30 seconds is not enough to pass stress tests
                        EnableMultipleHttp2Connections = true
                    } });

                    client = new WebViewIPC.WebViewIPCClient(channel);
                   
                    var events = client.CreateWebView(new CreateWebViewRequest { Id = Id, HtmlHostPath = HostHtmlPath, Markup = Markup, Group=Group, HostName = Dns.GetHostName(), Pid=Process.GetCurrentProcess().Id, ProcessName= Process.GetCurrentProcess().ProcessName}, cancellationToken: cts.Token); 
                    var completed = new ManualResetEventSlim();
                    Exception? exception = null;

                    Task.Factory.StartNew(async () =>
                    {
                        try
                        {
                            await foreach (var message in events.ResponseStream.ReadAllAsync())
                            {
                                var command = message.Response.Split(':')[0];
                                var data = message.Response[(message.Response.IndexOf(':') + 1)..];

                                try
                                {
                                    switch (command)
                                    {
                                        case "created":
                                            completed.Set();
                                            break;

                                        case "createFailed":
                                            exception = new Exception("WebView Create failed - Id must be unique");
                                            completed.Set();
                                            break;

                                        case "__bwv":
                                            OnWebMessageReceived?.Invoke(message.Url, message.Response);
                                            break;

                                        case "booted":
                                            lock (bootLock)
                                            {
                                                Client?.Shutdown(new IdMessageRequest { Id = Id });
                                                BlazorWebView.FireRefreshed(new RefreshedEventArgs(Guid.Parse(Id), ServerUri));
                                                cts.Cancel();
                                            }
                                            break;

                                        case "connected":
                                            BlazorWebView.FireConnected(new ConnectedEventArgs(Guid.Parse(Id), ServerUri));
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
                        BlazorWebView.FireDisconnected(new DisconnectedEventArgs(Guid.Parse(Id), ServerUri, exception));
                        throw exception;
                    }

                    Task.Factory.StartNew(async () =>
                    {
                        var files = client.FileReader();
                        try
                        {
                            await files.RequestStream.WriteAsync(new FileReadRequest { Init = new FileReadInitRequest { Id = Id } });

                            await foreach (var message in files.ResponseStream.ReadAllAsync(cts.Token))
                            {
                                try
                                {
                                    var path = message.Path[(message.Path.IndexOf("/") + 1)..];

                                    using (var stream = FileProvider.GetFileInfo(path).CreateReadStream() ?? null)
                                    {
                                        if (stream == null)
                                            await files.RequestStream.WriteAsync(new FileReadRequest { Data = new FileReadDataRequest { Id = Id, Path = message.Path, Data = ByteString.Empty } });
                                        else
                                        {
                                            var buffer = new Byte[8 * 1024];
                                            int bytesRead = 0;

                                            while ((bytesRead = await stream.ReadAsync(buffer)) > 0)
                                            {
                                                ByteString bs = ByteString.CopyFrom(buffer, 0, bytesRead);
                                                await files.RequestStream.WriteAsync(new FileReadRequest { Data = new FileReadDataRequest { Id = Id, Path = message.Path, Data = bs } });
                                            }
                                            await files.RequestStream.WriteAsync(new FileReadRequest { Data = new FileReadDataRequest { Id = Id, Path = message.Path, Data = ByteString.Empty } });
                                        }
                                    }

                                }
                                catch (FileNotFoundException)
                                {
                                    // TODO Warning to user?
                                    await files.RequestStream.WriteAsync(new FileReadRequest { Data = new FileReadDataRequest { Id = Id, Path = message.Path, Data = ByteString.Empty } });
                                }
                                catch (Exception ex)
                                {
                                    BlazorWebView.FireDisconnected(new DisconnectedEventArgs(Guid.Parse(Id), ServerUri, ex));
                                    await files.RequestStream.WriteAsync(new FileReadRequest { Data = new FileReadDataRequest { Id = Id, Path = message.Path, Data = ByteString.Empty } });
                                }
                            }
                        } catch (Exception ex)
                        {
                            BlazorWebView.FireDisconnected(new DisconnectedEventArgs(Guid.Parse(Id), ServerUri, ex));
                        }
                    }, cts.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
                }
                return client;
            }
        }

        public event EventHandler<string>? OnWebMessageReceived;

        private string GenMarkup(Uri uri,string id)
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

        public RemoteWebView(IBlazorWebView blazorWebView, Uri uri, string hostHtmlPath, Dispatcher dispatcher, IFileProvider fileProvider, string id,  string group = "", string markup = "")
        {
            BlazorWebView = blazorWebView;
            ServerUri = uri;
            HostHtmlPath = hostHtmlPath;
            Dispacher = dispatcher;
            FileProvider = fileProvider;
            Id = id;
            Markup = string.IsNullOrWhiteSpace(markup) ? GenMarkup(uri,id) : markup;
            Group = string.IsNullOrWhiteSpace(group) ? "test" : group;
        }

        public void NavigateToUrl(string _) { }

        public void SendMessage(string message)
        {
            Client?.SendMessage(new SendMessageRequest { Id = Id, Message = message });
        }

        public void Initialize()
        {
            _ = Client;
        }

    }
}
