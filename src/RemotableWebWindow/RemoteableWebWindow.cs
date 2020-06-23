using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;
using Grpc.Net.Client;
using BlazorWebView;
using BlazorWebView.Wpf;
using Google.Protobuf;
using System.Drawing;
using System.Collections.Generic;
using Newtonsoft.Json;
using Microsoft.JSInterop;
using Newtonsoft.Json.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;



namespace PeakSwc.RemoteableWebWindows
{
    public class RemotableWebWindow : IBlazorWebView 
    {
        #region private
        private readonly Uri uri;
        private readonly string hostHtmlPath;
        private readonly string hostname;

        private int bootCount = 0;
        private readonly object bootLock = new object();
        private string id = null;
        private string Id
        {
            get
            {
                if (id == null)
                    id = Guid.NewGuid().ToString();

                return id;
            }
        }
        private RemoteWebWindow.RemoteWebWindowClient client = null;
        
        private CancellationTokenSource cts = new CancellationTokenSource();
        #endregion


        public RemoteWebWindow.RemoteWebWindowClient Client {
            get
            {
                if (client == null)
                {
                    var channel = GrpcChannel.ForAddress(uri);

                    client = new RemoteWebWindow.RemoteWebWindowClient(channel);
                    var events = client.CreateWebWindow(new CreateWebWindowRequest { Id = Id, HtmlHostPath = hostHtmlPath, Hostname=hostname }, cancellationToken: cts.Token); // TODO parameter names
                    var completed = new ManualResetEventSlim();
                   
                    Task.Run(async () =>
                    {
                        try
                        {
                            await foreach (var message in events.ResponseStream.ReadAllAsync())
                            {
                                var command = message.Response.Split(':')[0];
                                var data = message.Response.Substring(message.Response.IndexOf(':')+1);

                                switch (command)
                                {
                                    case "created": 
                                        completed.Set();
                                        break;
                                    case "webmessage":
                                        if (data == "booted:")
                                        {
                                            lock (bootLock)
                                            {
                                                bootCount++;
                                            }
                                        }
                                       
                                        else

                                            OnWebMessageReceived?.Invoke(null, data);
                                        break;
                                    //case "location":
                                    //    LocationChangedEvent?.Invoke(null, JsonConvert.DeserializeObject<Point>( data));
                                    //    break;
                                    
                                }
                                                               
                            }
                        }
                        catch (RpcException ex) when (ex.StatusCode == StatusCode.Cancelled)
                        {

                            Console.WriteLine("Stream cancelled.");  //TODO
                        }
                    }, cts.Token);

                   

                    completed.Wait();

                    Task.Run(async () =>
                    {
                        var files = client.FileReader();

                        await files.RequestStream.WriteAsync(new FileReadRequest { Id = Id, Path = "Initialize" });

                        await foreach (var message in files.ResponseStream.ReadAllAsync())
                        {
                            if (File.Exists(message.Path))
                            {
                                var bytes = File.ReadAllBytes(message.Path);
                                await files.RequestStream.WriteAsync(new FileReadRequest { Id = Id, Path = message.Path, Data = ByteString.CopyFrom(bytes) });
                            }
                            else await files.RequestStream.WriteAsync(new FileReadRequest { Id = Id, Path = message.Path });

                        }

                    }, cts.Token);

                }
                return client;
            }
        }

        public event EventHandler<string> OnWebMessageReceived;


        public RemotableWebWindow(Uri uri, string hostHtmlPath)
        {
            this.uri = uri;
            this.hostHtmlPath = hostHtmlPath;
            this.hostname = Dns.GetHostName();
        }

        public void NavigateToUrl(string url) { }

        public void SendMessage(string message)
        {
            Client.SendMessage(new SendMessageRequest { Id=Id, Message = message });
        }


        public void ShowMessage(string title, string body)
        {
            Client.ShowMessage(new ShowMessageRequest { Id=Id, Body = body, Title = title });
        }
        private void Shutdown()
        {
            Client.Shutdown(new IdMessageRequest { Id = Id });
        }
        public Task WaitForExit()
        {
            return Task.Run(() =>
            {
                while (true)
                {
                    lock (bootLock)
                    {
                        if (bootCount >= 1)
                        {
                            bootCount = 0;
                            break;
                        }
                    }
                    Thread.Sleep(1000);
                }
                Shutdown();
            });
                   

        }

        public void Initialize(Action<WebViewOptions> configure)
        {
            _ = Client;
        }

        public void Invoke(Action callback)
        {
            callback.Invoke();
        }
      
    }
}
