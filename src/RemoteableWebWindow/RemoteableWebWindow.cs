﻿//$(UserProfile)\.nuget\packages\$(AssemblyName.toLower())\$(Version)\lib
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;
using Grpc.Net.Client;
using BlazorWebView;

using Google.Protobuf;
using System.Drawing;
using System.Collections.Generic;
using Newtonsoft.Json;
using Microsoft.JSInterop;
using Newtonsoft.Json.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Reflection;

namespace PeakSwc.RemoteableWebWindows
{
    public class RemotableWebWindow : IBlazorWebView 
    {
        #region private
        private readonly Uri uri;
        private readonly string hostHtmlPath;
        private readonly string hostname;
        private readonly object bootLock = new object();
        private string id = null;
        public string Id
        {
            get
            {
                if (id == null)
                    id = Guid.NewGuid().ToString();

                return id;
            }
        }
        private RemoteWebWindow.RemoteWebWindowClient client = null;
        
        // TODO
        private readonly CancellationTokenSource cts = new CancellationTokenSource();
        #endregion

        // TODO Use stream
        public Func<string, byte[]> FrameworkFileResolver { get; set; } = SupplyFrameworkFile;

        public static Byte[] SupplyFrameworkFile(string uri)
        {
            if (Path.GetFileName(uri) == "remote.blazor.desktop.js")
            {
                var assembly = Assembly.GetExecutingAssembly();

                using (MemoryStream ms = new MemoryStream())
                {
                    assembly.GetManifestResourceStream("PeakSwc.RemoteableWebWindows.remote.blazor.desktop.js").CopyTo(ms);
                    return ms.ToArray();
                }
            }

            if (File.Exists(uri))
            {
                return File.ReadAllBytes(uri);
            }
            return null;
        }

        public IJSRuntime JSRuntime { get; set; }

        private  RemoteWebWindow.RemoteWebWindowClient Client {
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
                                                Shutdown();
                                                OnDisconnected?.Invoke(this, null);
                                            }
                                        }
                                        else if (data == "connected:")
                                            OnConnected?.Invoke(this, null);

                                        else

                                            OnWebMessageReceived?.Invoke(null, data);
                                        break;
                                   
                                    
                                }
                                                               
                            }
                        }
                        catch (RpcException ex) when (ex.StatusCode == StatusCode.Cancelled)
                        {
                            OnDisconnected?.Invoke(this, null);
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
                            var bytes = FrameworkFileResolver(message.Path);
                            await files.RequestStream.WriteAsync(new FileReadRequest { Id = Id, Path = message.Path, Data = bytes == null ? null : ByteString.CopyFrom(bytes) });
                        }

                    }, cts.Token);

                }
                return client;
            }
        }

        public event EventHandler<string> OnWebMessageReceived;
        public event EventHandler OnConnected;
        public event EventHandler OnDisconnected;

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
            JSRuntime?.InvokeVoidAsync($"RemoteWebWindow.showMessage", new object[] { "title", body });
        }
        private void Shutdown()
        {
            Client.Shutdown(new IdMessageRequest { Id = Id });
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
