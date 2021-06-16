//$(UserProfile)\.nuget\packages\$(AssemblyName.toLower())\$(Version)\lib
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;
using Grpc.Net.Client;
using Google.Protobuf;
using System.Net;
using System.Reflection;
using System.Windows;
using Microsoft.AspNetCore.Components;
using System.Diagnostics;
using RemoteBlazorWebView.Wpf;
using System.Collections.Generic;
using System.Xml.Linq;
using static PeakSwc.RemoteableWebWindows.StaticWebAssetsReader;
using System.Linq;
using Microsoft.AspNetCore.Http;

namespace PeakSwc.RemoteableWebWindows
{
    internal static class StaticWebAssetsReader
    {
        private const string ManifestRootElementName = "StaticWebAssets";
        private const string VersionAttributeName = "Version";
        private const string ContentRootElementName = "ContentRoot";

        internal static IEnumerable<ContentRootMapping> Parse(Stream manifest)
        {
            var document = XDocument.Load(manifest);
            if (!string.Equals(document.Root!.Name.LocalName, ManifestRootElementName, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Invalid manifest format. Manifest root must be '{ManifestRootElementName}'");
            }

            var version = document.Root.Attribute(VersionAttributeName);
            if (version == null)
            {
                throw new InvalidOperationException($"Invalid manifest format. Manifest root element must contain a version '{VersionAttributeName}' attribute");
            }

            if (version.Value != "1.0")
            {
                throw new InvalidOperationException($"Unknown manifest version. Manifest version must be '1.0'");
            }

            foreach (var element in document.Root.Elements())
            {
                if (!string.Equals(element.Name.LocalName, ContentRootElementName, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException($"Invalid manifest format. Invalid element '{element.Name.LocalName}'. All StaticWebAssetsManifestName child elements must be '{ContentRootElementName}' elements.");
                }
                if (!element.IsEmpty)
                {
                    throw new InvalidOperationException($"Invalid manifest format. {ContentRootElementName} can't have content.");
                }

                var basePath = ParseRequiredAttribute(element, "BasePath");
                var path = ParseRequiredAttribute(element, "Path");
                yield return new ContentRootMapping(basePath, path);
            }
        }

        private static string ParseRequiredAttribute(XElement element, string attributeName)
        {
            var attribute = element.Attribute(attributeName);
            if (attribute == null)
            {
                throw new InvalidOperationException($"Invalid manifest format. Missing {attributeName} attribute in '{ContentRootElementName}' element.");
            }
            return attribute.Value;
        }

        internal readonly struct ContentRootMapping
        {
            public ContentRootMapping(string basePath, string path)
            {
                BasePath = basePath;
                Path = path;
            }

            public string BasePath { get; }
            public string Path { get; }
        }
    }
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
        private Func<string, Stream?> FrameworkFileResolver { get; set; }
        // TODO unused
        private readonly CancellationTokenSource cts = new();

        private static List<ContentRootMapping>? rootMap;
        
        
        #endregion

        public Uri? ServerUri { get; set; }
        public string HostHtmlPath { get; set; } = "";
        public string Id { get; set; } = "";

        private static string NormalizePath(string path)
        {
            path = path.Replace('\\', '/');
            return path.StartsWith('/') ? path : "/" + path;
        }
        private static readonly StringComparison FilePathComparison = OperatingSystem.IsWindows() ?
                StringComparison.OrdinalIgnoreCase :
                StringComparison.Ordinal;

        private static bool StartsWithBasePath(string subpath, PathString basePath, out PathString rest)
        {
            return new PathString(subpath).StartsWithSegments(basePath, FilePathComparison, out rest);
        }

        public Stream? SupplyFrameworkFile(string uri)
        {
            try
            {
                if (Path.GetFileName(uri) == "remote.blazor.desktop.js")
                    return Assembly.GetExecutingAssembly().GetManifestResourceStream("PeakSwc.RemoteableWebWindows.remote.blazor.desktop.js");

                if (File.Exists(uri))
                    return File.OpenRead(uri);

                else
                {
                    if (rootMap == null)
                    {
                        var stream = GetManifestStream();
                        if (stream != null)
                            rootMap = StaticWebAssetsReader.Parse(stream).ToList();
                    }

                    if (rootMap == null)
                        return null;

                    foreach (var m in rootMap)
                    {
                        if (NormalizePath(m.BasePath) == "/")
                        {
                            var f = m.Path + uri.Substring(uri.IndexOf('/'));
                            if (File.Exists(f))
                                return File.OpenRead(f);
                        }
                       
                        if (StartsWithBasePath(uri.Substring(uri.IndexOf('/')), NormalizePath(m.BasePath), out PathString mappedPath))
                        {
                            var f = m.Path + mappedPath;
                        
                            if (File.Exists(f))
                                return File.OpenRead(f);
                        }
                           
                    }
                       
                }
            }
            catch (Exception ex) {
                var m = ex.Message;
                return null;  }
               
            return null;
        }

        private static string? ResolveRelativeToAssembly()
        {
            var assembly = Assembly.GetEntryAssembly();
            if (string.IsNullOrEmpty(assembly?.Location))
            {
                return null;
            }

            var name = Path.GetFileNameWithoutExtension(assembly.Location);

            return Path.Combine(Path.GetDirectoryName(assembly.Location)!, $"{name}.StaticWebAssets.xml");
        }

        static Stream? GetManifestStream()
        {
            try
            {
                var filePath = ResolveRelativeToAssembly();

                if (filePath != null && File.Exists(filePath))
                {
                    return File.OpenRead(filePath);
                }
                else
                {
                    // A missing manifest might simply mean that the feature is not enabled, so we simply
                    // return early. Misconfigurations will be uncommon given that the entire process is automated
                    // at build time.
                    return null;
                }
            }
            catch
            {
                return null;
            }
        }

        //public IJSRuntime? JSRuntime { get; set; }

        protected  RemoteWebWindow.RemoteWebWindowClient? Client {
            get
            {
                if (ServerUri == null) return null;

                if (client == null)
                {
                    var channel = GrpcChannel.ForAddress(ServerUri);

                    client = new RemoteWebWindow.RemoteWebWindowClient(channel);
                    var events = client.CreateWebWindow(new CreateWebWindowRequest { Id = Id, HtmlHostPath = HostHtmlPath, Hostname=hostname }, cancellationToken: cts.Token); // TODO parameter names
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
                                               
                                                OnConnected?.Invoke(this, Id );
                                            else
                                                OnWebMessageReceived?.Invoke(this, data);
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
                            OnDisconnected?.Invoke(this, Id );
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
                        

                        // TODO Use multiple threads to read files
                        //_ = Task.Run(async () =>
                        //{
                        //    await foreach (var message in files.ResponseStream.ReadAllAsync())
                        //    {
                        //        try
                        //        {
                        //            // TODO Missing file
                        //            var bytes = FrameworkFileResolver(message.Path) ?? null;
                        //            ByteString temp = ByteString.Empty;
                        //            if (bytes != null)
                        //                temp = ByteString.FromStream(bytes);
                        //            await files.RequestStream.WriteAsync(new FileReadRequest { Id = Id, Path = message.Path, Data = temp });
                        //        }
                        //        catch (Exception ex)
                        //        {
                        //            var m = ex.Message;
                        //        }
                        //    }
                              
                        //});

                        await foreach (var message in files.ResponseStream.ReadAllAsync())
                        {
                        try
                        {
                            var bytes = FrameworkFileResolver(message.Path) ?? null;
                            ByteString temp = ByteString.Empty;
                            if (bytes != null)
                                temp = ByteString.FromStream(bytes);
                            await files.RequestStream.WriteAsync(new FileReadRequest { Id = Id, Path = message.Path, Data = temp });
                        }
                        catch (Exception ex)
                        {
                            var m = ex.Message;
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
        public RemotableWebWindow() {
            hostname = Dns.GetHostName();
            FrameworkFileResolver = SupplyFrameworkFile;
           
        }

        //public RemotableWebWindow(Uri uri, string hostHtmlPath, Guid id = default(Guid))
        //{
        //    Id = id == default(Guid) ? Guid.NewGuid().ToString() : id.ToString();
        //    this.uri = uri;
        //    this.hostHtmlPath = hostHtmlPath;
        //    _ = Client;

        //}

        public void NavigateToUrl(string _) { }

        public void SendMessage(string message)
        {
            Client?.SendMessage(new SendMessageRequest { Id=Id, Message = message });
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
