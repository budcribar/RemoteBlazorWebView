//$(UserProfile)\.nuget\packages\$(AssemblyName.toLower())\$(Version)\lib
using Google.Protobuf;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.FileProviders;
using System;
using System.Diagnostics;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace PeakSWC.RemoteableWebView
{
    public class RemoteableWebView // : IBlazorWebView 
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
                    p = Process.Start(new ProcessStartInfo("cmd", $"/c start microsoft-edge:" + url) { CreateNoWindow = true });
                }
                catch (Exception)
                {
                    p = null;
                }
                return p;
            });

        }

        #region private

        private readonly string hostname;
        private readonly object bootLock = new();
        private string Group { get; init; }
        private RemoteWebView.RemoteWebViewClient? client = null;
        private readonly CancellationTokenSource cts = new();
        private IFileProvider FileProvider { get; }
        #endregion

        public Uri? ServerUri { get; }
        public string HostHtmlPath { get; } = string.Empty;
        public string Id { get; }
       
        public Dispatcher? Dispacher { get; set; }
        //public IJSRuntime? JSRuntime { get; set; }

        protected RemoteWebView.RemoteWebViewClient? Client
        {
            get
            {
                if (ServerUri == null) return null;

                if (client == null)
                {
                    var channel = GrpcChannel.ForAddress(ServerUri);

                    client = new RemoteWebView.RemoteWebViewClient(channel);
                   
                    var events = client.CreateWebView(new CreateWebViewRequest { Id = Id, HtmlHostPath = HostHtmlPath, Hostname = hostname, Group=Group }, cancellationToken: cts.Token); 
                    var completed = new ManualResetEventSlim();
                    Exception? exception = null;

                    Task.Run(async () =>
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
                                                Shutdown();

                                                OnDisconnected?.Invoke(this, Id);
                                            }
                                            break;

                                        case "connected":
                                            OnConnected?.Invoke(this, Id);
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
                        catch (RpcException ex) when (ex.StatusCode == StatusCode.Cancelled)
                        {
                            OnDisconnected?.Invoke(this, Id);
                            exception = ex;
                            completed.Set();
                        }
                        catch (RpcException ex) when (ex.StatusCode == StatusCode.Unavailable)
                        {
                            exception = ex;
                            completed.Set();
                        }
                    }, cts.Token);

                    completed.Wait();

                    if (exception != null)
                        throw (exception);

                    Task.Run(async () =>
                    {
                        var files = client.FileReader();
                     
                        await files.RequestStream.WriteAsync(new FileReadRequest { Init = new FileReadInitRequest { Id = Id } });

                        await foreach (var message in files.ResponseStream.ReadAllAsync())
                        {
                            try
                            {
                                var path = message.Path[(message.Path.IndexOf("/") + 1)..];

                                var stream = FileProvider.GetFileInfo(path).CreateReadStream() ?? null;
                                if (stream == null)
                                    await files.RequestStream.WriteAsync(new FileReadRequest { Data = new FileReadDataRequest { Id = Id, Path = message.Path, Data = ByteString.Empty } });
                                else
                                {
                                    var buffer = new Byte[1*1024];
                                    int bytesRead = 0;

                                    while ((bytesRead = await stream.ReadAsync(buffer)) > 0)
                                    {
                                        ByteString bs = ByteString.CopyFrom(buffer, 0, bytesRead);
                                        await files.RequestStream.WriteAsync(new FileReadRequest { Data = new FileReadDataRequest { Id = Id, Path = message.Path, Data = bs } });
                                    }
                                    await files.RequestStream.WriteAsync(new FileReadRequest { Data = new FileReadDataRequest { Id = Id, Path = message.Path, Data = ByteString.Empty } });
                                }
                            }
                            catch (Exception)
                            {
                                await files.RequestStream.WriteAsync(new FileReadRequest { Data = new FileReadDataRequest { Id = Id, Path = message.Path, Data = ByteString.Empty } });
                            }
                        }

                    }, cts.Token);

                }
                return client;
            }
        }

        public event EventHandler<string>? OnWebMessageReceived;
        public event EventHandler<string>? OnConnected;
        public event EventHandler<string>? OnDisconnected;

        public RemoteableWebView(Uri uri, string hostHtmlPath, Dispatcher dispatcher, IFileProvider fileProvider, string id,  string group)
        {
            ServerUri = uri;
            HostHtmlPath = hostHtmlPath;
            Dispacher = dispatcher;
            FileProvider = fileProvider;
            Id = id;
            hostname = Dns.GetHostName();
            Group = group;
        }

        public void NavigateToUrl(string _) { }

        public void SendMessage(string message)
        {
            Client?.SendMessage(new SendMessageRequest { Id = Id, Message = message });
        }

        private void Shutdown()
        {
            Client?.Shutdown(new IdMessageRequest { Id = Id });
        }

        public void Initialize()
        {
            _ = Client;
        }

    }
}
