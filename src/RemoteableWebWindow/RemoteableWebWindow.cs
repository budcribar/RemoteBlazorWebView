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
    public class RemotableWebWindow // : IBlazorWebView 
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

        public static void StartBrowser(IBlazorWebView blazorWebView)
        {
            var url = $"{blazorWebView.ServerUri}app/{blazorWebView.Id}";
            try
            {
                Process.Start(new ProcessStartInfo("cmd", $"/c start microsoft-edge:" + url) { CreateNoWindow = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Unable to open hyperlink. Error:{ex.Message}");
            }
        }

        #region private

        private readonly string hostname;
        private readonly object bootLock = new();
        public Dispatcher? Dispacher { get; set; }

        private RemoteWebWindow.RemoteWebWindowClient? client = null;
        private readonly CancellationTokenSource cts = new();

        #endregion

        public IFileProvider FileProvider { get; set; }
        public Uri? ServerUri { get; set; }
        public string HostHtmlPath { get; set; } = string.Empty;
        public string Id { get; set; } = string.Empty;

        //public IJSRuntime? JSRuntime { get; set; }

        protected RemoteWebWindow.RemoteWebWindowClient? Client
        {
            get
            {
                if (ServerUri == null) return null;

                if (client == null)
                {
                    var channel = GrpcChannel.ForAddress(ServerUri);

                    client = new RemoteWebWindow.RemoteWebWindowClient(channel);
                    var events = client.CreateWebWindow(new CreateWebWindowRequest { Id = Id, HtmlHostPath = HostHtmlPath, Hostname = hostname }, cancellationToken: cts.Token); 
                    var completed = new ManualResetEventSlim();
                    var createFailed = false;

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
                                            createFailed = true;
                                            completed.Set();
                                            break;

                                        case "__bwv":
                                            OnWebMessageReceived?.Invoke(message.Url, message.Response);
                                            break;

                                        case "webmessage":
                                            if (data == "booted:")
                                            {
                                                lock (bootLock)
                                                {
                                                    Shutdown();

                                                    OnDisconnected?.Invoke(this, Id);
                                                }
                                            }
                                            else if (data == "connected:")

                                                OnConnected?.Invoke(this, Id);
                                              
                                            break;
                                    }
                                }
                                catch (Exception ex)
                                {
                                    var m = ex.Message;

                                }
                            }
                        }
                        catch (RpcException ex) when (ex.StatusCode == StatusCode.Cancelled)
                        {
                            OnDisconnected?.Invoke(this, Id);
                            Console.WriteLine("Stream cancelled.");  //TODO
                        }
                    }, cts.Token);

                    completed.Wait();

                    if (createFailed)
                        return null;

                    Task.Run(async () =>
                    {
                        var files = client.FileReader();

                        await files.RequestStream.WriteAsync(new FileReadRequest { Id = Id, Path = "Initialize" });

                        await foreach (var message in files.ResponseStream.ReadAllAsync())
                        {
                            try
                            {
                                var path = message.Path.Substring(message.Path.IndexOf("/")+1);

                                var bytes = FileProvider.GetFileInfo(path).CreateReadStream() ?? null;
                                ByteString temp = ByteString.Empty;
                                if (bytes != null)
                                    temp = ByteString.FromStream(bytes);
                                await files.RequestStream.WriteAsync(new FileReadRequest { Id = Id, Path = message.Path, Data = temp });
                            }
                            catch (Exception)
                            {
                                await files.RequestStream.WriteAsync(new FileReadRequest { Id = Id, Path = message.Path, Data = ByteString.Empty });
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
        public RemotableWebWindow()
        {
            hostname = Dns.GetHostName();
        }

        public void NavigateToUrl(string _) { }

        public void SendMessage(string message)
        {
            Client?.SendMessage(new SendMessageRequest { Id = Id, Message = message });
        }

        //public void ShowMessage(string title, string body)
        //{
        //    //JSRuntime?.InvokeVoidAsync($"RemoteWebWindow.showMessage", new object[] { "title", body });
        //}
        private void Shutdown()
        {
            Client?.Shutdown(new IdMessageRequest { Id = Id });
        }

        public void Initialize()
        {
            _ = Client;
        }

        //public void Invoke(Action callback)
        //{
        //    callback.Invoke();
        //}

    }
}
