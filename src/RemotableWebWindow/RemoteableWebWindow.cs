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

// TODO 1. Had to move css and sample data from BlazorWebViewTutorial.Shared to BlazorWebViewTutorial.WpfApp wwwroot
//      2. modify index.html to reference <script src="remote.blazor.desktop.js"></script>
//      3. link in remote.blazor.desktop.js

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
        
        private readonly CancellationTokenSource cts = new CancellationTokenSource();
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

        public RemotableWebWindow(Uri uri, string hostHtmlPath)
        {
            this.uri = uri;
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

        public void Initialize(Action<WebViewOptions> configure)
        {
            //var options = new WebViewOptions();
            //configure.Invoke(options);

            //foreach (var (schemeName, handler) in options.SchemeHandlers)
            //{
            //    this.AddCustomScheme(schemeName, handler);
            //}

            //if (!BlazorWebViewNative_Initialize(this.blazorWebView))
            //{
            //    throw new InvalidOperationException(this.lastErrorMessage);
            //}
        }

        public void Invoke(Action callback)
        {
            callback.Invoke();
            //throw new NotImplementedException();
        }

        /// <summary>
        /// A callback delegate to handle a Resource request.
        /// </summary>
        /// <param name="url">The url to request a resource for.</param>
        /// <param name="numBytes">The number of bytes of the resource.</param>
        /// <param name="contentType">The content type of the resource.</param>
        /// <returns>A pointer to a stream.</returns>
        [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = CharSet.Auto)]
        private delegate IntPtr WebResourceRequestedCallback(string url, out int numBytes, out string contentType);

        //private void AddCustomScheme(string scheme, ResolveWebResourceDelegate requestHandler)
        //{
        //    // Because of WKWebView limitations, this can only be called during the constructor
        //    // before the first call to Show. To enforce this, it's private and is only called
        //    // in response to the constructor options.
        //    WebResourceRequestedCallback callback = (string url, out int numBytes, out string contentType) =>
        //    {
        //        var responseStream = requestHandler(url, out contentType, out Encoding encoding);
        //        if (responseStream == null)
        //        {
        //            // Webview should pass through request to normal handlers (e.g., network)
        //            // or handle as 404 otherwise
        //            numBytes = 0;
        //            return default;
        //        }

        //        // Read the stream into memory and serve the bytes
        //        // In the future, it would be possible to pass the stream through into C++
        //        using (responseStream)
        //        using (var ms = new MemoryStream())
        //        {
        //            responseStream.CopyTo(ms);

        //            numBytes = (int)ms.Position;
        //            var buffer = Marshal.AllocCoTaskMem(numBytes);
        //            Marshal.Copy(ms.GetBuffer(), 0, buffer, numBytes);
        //            return buffer;
        //        }
        //    };

        //    this.gcHandlesToFree.Add(GCHandle.Alloc(callback));
        //    BlazorWebViewNative_AddCustomScheme(this.blazorWebView, scheme, callback);
        //}
    }
}
