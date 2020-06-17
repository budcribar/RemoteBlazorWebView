using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;
using Grpc.Net.Client;

using Google.Protobuf;
using System.Drawing;
using System.Collections.Generic;
using Newtonsoft.Json;
using Microsoft.JSInterop;
using Newtonsoft.Json.Linq;
using System.Net;

namespace PeakSwc.RemoteableWebWindows
{
    public class RemotableWebWindow : IWebWindow
    {
        #region private
        private readonly Uri uri;
        private readonly string windowTitle;
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
        private readonly CancellationTokenSource cts = new CancellationTokenSource();
        #endregion


        public RemoteWebWindow.RemoteWebWindowClient Client {
            get
            {
                if (client == null)
                {
                    var channel = GrpcChannel.ForAddress(uri);

                    client = new RemoteWebWindow.RemoteWebWindowClient(channel);
                    var events = client.CreateWebWindow(new CreateWebWindowRequest { Id = Id, HtmlHostPath = hostHtmlPath, Title = windowTitle, Hostname=hostname }, cancellationToken: cts.Token); // TODO parameter names
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
                                                //if (bootCount >= 1)
                                                //    cts.Cancel();
                                            }
                                        }
                                        else if (data.StartsWith("size:"))
                                        {
                                            var size = data.Replace("size:","");
                                            var jo = JsonConvert.DeserializeObject<JObject>(size);

                                            await Task.Run(() =>
                                            {
                                                // Hangs otherwise
                                                SizeChangedEvent?.Invoke(null, new Size((int)jo["Width"], (int)jo["Height"]));
                                            });

                                            
                                        }
                                        else if (data.StartsWith("location:"))
                                        {
                                            var location = data.Replace("location:", "");

                                            

                                            var jo = JsonConvert.DeserializeObject<JObject>(location);
                                            //var x = JsonConvert.DeserializeObject<Point>(location);

                                            await Task.Run(() =>
                                            {
                                                // TODO Hangs otherwise
                                                LocationChangedEvent?.Invoke(null, new Point((int)jo["X"], (int)jo["Y"]));
                                            });


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

        public IJSRuntime JSRuntime { get; set; }

        public int Height { get => JSRuntime.InvokeAsync<int>("RemoteWebWindow.height").Result; set => JSRuntime.InvokeVoidAsync("RemoteWebWindow.setHeight", new object[] { value }); }
        public int Left { get => JSRuntime.InvokeAsync<int>("RemoteWebWindow.left").Result; set => JSRuntime.InvokeVoidAsync("RemoteWebWindow.setLeft", new object[] { value }); }
        public Point Location { get { var l = JSRuntime.InvokeAsync<Point>("RemoteWebWindow.location").Result; return new Point(l.X, l.Y); } set => JSRuntime.InvokeVoidAsync("RemoteWebWindow.setLocation", new object[] { value }); }

        public bool Resizable { get => true;  set { }  }

        public uint ScreenDpi { get => 96; }

        public Size Size { get { var l = JSRuntime.InvokeAsync<Size>("RemoteWebWindow.size").Result; return new Size(l.Width, l.Height); } set => JSRuntime.InvokeVoidAsync("RemoteWebWindow.setSize", new object[] { value }); }
       
        public string Title { get => JSRuntime.InvokeAsync<string>("RemoteWebWindow.title").Result; set => JSRuntime.InvokeVoidAsync("RemoteWebWindow.setTitle", new object[] { value }); }

        public int Top { get => JSRuntime.InvokeAsync<int>("RemoteWebWindow.top").Result; set => JSRuntime.InvokeVoidAsync("RemoteWebWindow.setTop", new object[] { value }); }

        public bool Topmost { get => false; set { } }

        public int Width { get => JSRuntime.InvokeAsync<int>("RemoteWebWindow.width").Result; set => JSRuntime.InvokeVoidAsync("RemoteWebWindow.setWidth", new object[] { value }); }

        public event EventHandler<string> OnWebMessageReceived;

        private event EventHandler<Point> LocationChangedEvent;
        private readonly object eventLock = new object();

        public event EventHandler<Point> LocationChanged
        {
            add
            {
                lock(eventLock)
                {
                    JSRuntime.InvokeVoidAsync("RemoteWebWindow.setLocationEventHandlerAttached", new object[] { true });
                    LocationChangedEvent += value;
                }
               
            }
            remove
            {
                lock (eventLock)
                {
                    LocationChangedEvent -= value;

                    if (LocationChangedEvent.GetInvocationList().Length == 0)
                        JSRuntime.InvokeVoidAsync("RemoteWebWindow.setLocationEventHandlerAttached", new object[] { false });
                }
            }
        }

        private event EventHandler<Size> SizeChangedEvent;
        public event EventHandler<Size> SizeChanged
        {
            add
            {
                lock (eventLock)
                {
                    JSRuntime.InvokeVoidAsync("RemoteWebWindow.setResizeEventHandlerAttached", new object[] { true });
                    SizeChangedEvent += value;
                }

            }
            remove
            {
                lock (eventLock)
                {
                    SizeChangedEvent -= value;

                    if (LocationChangedEvent.GetInvocationList().Length == 0)
                        JSRuntime.InvokeVoidAsync("RemoteWebWindow.setResizeEventHandlerAttached", new object[] { false });
                }
            }
        }

        public RemotableWebWindow(Uri uri, string windowTitle, string hostHtmlPath)
        {
            this.uri = uri;
            this.windowTitle = windowTitle;
            this.hostHtmlPath = hostHtmlPath;
            this.hostname = Dns.GetHostName();
        }

        public void NavigateToUrl(string url)
        {
           Client.NavigateToUrl(new UrlMessageRequest {  Id=Id, Url = url });
        }

        public void SendMessage(string message)
        {
            Client.SendMessage(new SendMessageRequest { Id=Id, Message = message });
        }

        public void Show()
        {
            // TODO
            Client.Show(new IdMessageRequest { Id=Id });
        }

        public void ShowMessage(string title, string body)
        {
            Client.ShowMessage(new ShowMessageRequest { Id=Id, Body = body, Title = title });
        }

        public void WaitForExit()
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

        }

        public void NavigateToLocalFile(string path)
        {
            // TODO
            var absolutePath = Path.GetFullPath(path);
            var url = new Uri(absolutePath, UriKind.Absolute);
            Client.NavigateToUrl(new UrlMessageRequest { Id = Id, Url = url.ToString() });
           
        }

        public void SetIconFile(string filename)
        {
            // TODO           
        }

        public void NavigateToString(string content)
        {
            // TODO
        }
    }
}
