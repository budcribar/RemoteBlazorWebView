using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using Microsoft.JSInterop.Infrastructure;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;


namespace PeakSWC.RemoteBlazorWebView.Windows
{
    public static class ComponentsDesktop
    {
        record RootComponent
        {
            public Type ComponentType { get; init; }
            public ParameterView Parameters { get; set; }
        }

        internal static string? InitialUriAbsolute { get; private set; }
        internal static string? BaseUriAbsolute { get; private set; }
        internal static WebViewJSRuntime? DesktopJSRuntime { get; private set; }
        internal static WebViewRenderer? DesktopRenderer { get; private set; }
        internal static IBlazorWebWindowBase? BlazorWebWindow { get; private set; }
        internal static DesktopDispatcher? Dispatcher { get; private set; }

        private static PageContext? _currentPageContext;
        private static readonly Dictionary<string, RootComponent> _rootComponentsBySelector = new();
        private static  IpcReceiver? _ipcReceiver;
        private static  IpcSender? _ipcSender;
        private static  Uri? _appBaseUri;
        private static  IServiceProvider _provider;

        /// <summary>
        /// Adds a root component to the attached page.
        /// </summary>
        /// <param name="componentType">The type of the root component. This must implement <see cref="IComponent"/>.</param>
        /// <param name="selector">The CSS selector describing where in the page the component should be placed.</param>
        /// <param name="parameters">Parameters for the component.</param>
        public static Task AddRootComponentAsync(Type componentType, string selector, ParameterView parameters)
        {
            var rootComponent = new RootComponent { ComponentType = componentType, Parameters = parameters };
            if (!_rootComponentsBySelector.TryAdd(selector, rootComponent))
            {
                throw new InvalidOperationException($"There is already a root component with selector '{selector}'.");
            }

            // If the page is already attached, add the root component to it now. Otherwise we'll
            // add it when the page attaches later.
            if (_currentPageContext != null)
            {
                return Dispatcher.InvokeAsync(() => _currentPageContext.Renderer.AddRootComponentAsync(componentType, selector, parameters));
            }
            else
            {
                return Task.CompletedTask;
            }
        }


        private static void SendMessage(string message)
        {
            BlazorWebWindow?.SendWebMessageBase(message);
        }

        public static void Run<TStartup>(IBlazorWebWindowBase blazorWebWindow)
        {
            var rbww = blazorWebWindow as RemoteBlazorWebWindow;
            _appBaseUri = rbww == null ? null : rbww.Uri;
            BlazorWebWindow = blazorWebWindow;

            blazorWebWindow.WebMessageReceived += BlazorWebWindow_WebMessageReceived;

            CancellationTokenSource appLifetimeCts = new CancellationTokenSource();

            Dispatcher = new DesktopDispatcher(appLifetimeCts.Token);

            Dispatcher.Context.UnhandledException += (sender, exception) =>
            {
                UnhandledException(exception);
            };

            // _root

            DesktopJSRuntime = new WebViewJSRuntime();

            _ipcSender = new IpcSender(Dispatcher, SendMessage);
            _ipcReceiver = new IpcReceiver(AttachToPageAsync);

            
            DesktopJSRuntime.AttachToWebView(_ipcSender);

            BlazorWebWindow.PlatformDispatcher = Dispatcher;
            BlazorWebWindow.JSRuntime = DesktopJSRuntime;

            var configurationBuilder = new ConfigurationBuilder()
               .SetBasePath(Directory.GetCurrentDirectory())
               .AddJsonFile("appsettings.json", optional: true);


            var serviceCollection = new ServiceCollection();
            serviceCollection.AddSingleton<IConfiguration>(configurationBuilder.Build());
            serviceCollection.AddLogging(configure => configure.AddConsole());
            serviceCollection.AddSingleton<NavigationManager>(new WebViewNavigationManager());
            // TODO attach to webview
            serviceCollection.AddSingleton<IJSRuntime>(DesktopJSRuntime);
            serviceCollection.AddSingleton<INavigationInterception, WebViewNavigationInterception>();

            BlazorWebWindow?.GetType().GetInterfaces().Where(x => x.Name == nameof(IBlazorWebWindowBase) || x.GetInterface(nameof(IBlazorWebWindowBase)) != null).ToList().ForEach(x =>
               serviceCollection.AddSingleton(x, BlazorWebWindow));

            var startup = new ConventionBasedStartup(Activator.CreateInstance(typeof(TStartup)));
            startup.ConfigureServices(serviceCollection);

            _provider = serviceCollection.BuildServiceProvider();
            var builder = new DesktopApplicationBuilder(_provider);
            startup.Configure(builder, _provider);

            builder.Entries.ForEach(x => AddRootComponentAsync(x.componentType, x.domElementSelector, ParameterView.Empty));

            try
            {
                
                BlazorWebWindow?.LoadBase(BlazorAppScheme + "://app/");
                BlazorWebWindow?.WaitForClose();
            }
            finally
            {
                appLifetimeCts.Cancel();
            }
        }

        private static void BlazorWebWindow_WebMessageReceived(object? sender, string e)
        {
            // TODO Need the real url
            MessageReceived(_appBaseUri, e);
        }

        public static Action<WebWindowOptions> StandardOptions(string hostHtmlPath)
        {
            return (options) => { 
                var contentRootAbsolute = Path.GetDirectoryName(Path.GetFullPath(hostHtmlPath));

                options.CustomSchemeHandlers.Add(BlazorAppScheme, (string url, out string contentType) =>
                {
                    // TODO: Only intercept for the hostname 'app' and passthrough for others
                    // TODO: Prevent directory traversal?
                    var appFile = Path.Combine(contentRootAbsolute ?? "", new Uri(url).AbsolutePath.Substring(1));
                    if (appFile == contentRootAbsolute)
                    {
                        appFile = hostHtmlPath;
                    }

                    contentType = GetContentType(appFile);
                    return File.Exists(appFile) ? File.OpenRead(appFile) : null;
                });

                // framework:// is resolved as embedded resources
                options.CustomSchemeHandlers.Add("framework", (string url, out string contentType) =>
                {
                    contentType = GetContentType(url);
                    return SupplyFrameworkFile(url);
                });
            };
        }

        public static void Run<TStartup>(string windowTitle, string hostHtmlPath, bool fullscreen = false, int x = 0, int y = 0, int width = 800, int height = 600)
        {
            BlazorWebWindow = new WebWindow(windowTitle, StandardOptions(hostHtmlPath), width, height, x, y, fullscreen);

            Run<TStartup>(BlazorWebWindow);
        }

        private static string GetContentType(string url)
        {
            var ext = Path.GetExtension(url);
            return MimeTypes.GetMimeType(ext);
        }

        private static string BlazorAppScheme
        {
            get
            {
                // On Windows, we can't use a custom scheme to host the initial HTML,
                // because webview2 won't let you do top-level navigation to such a URL.
                // On Linux/Mac, we must use a custom scheme, because their webviews
                // don't have a way to intercept http:// scheme requests.
                return RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                    ? "http"
                    : "app";
            }
        }

        /// <summary>
        /// Notifies the <see cref="WebViewManager"/> about a message from JavaScript running within the web view.
        /// </summary>
        /// <param name="sourceUri">The source URI for the message.</param>
        /// <param name="message">The message.</param>
        private static void MessageReceived(Uri sourceUri, string message)
        {
            if (!_appBaseUri.IsBaseOf(sourceUri))
            {
                // It's important that we ignore messages from other origins, otherwise if the webview
                // navigates to a remote location, it could send commands that execute locally
                return;
            }

            _ = Dispatcher?.InvokeAsync(async () =>
            {
                // TODO: Verify this produces the correct exception-surfacing behaviors.
                // For example, JS interop exceptions should flow back into JS, whereas
                // renderer exceptions should be fatal.
                try
                {
                    await _ipcReceiver.OnMessageReceivedAsync(_currentPageContext, message);
                }
                catch (Exception ex)
                {
                    _ipcSender.NotifyUnhandledException(ex);
                    throw;
                }
            });
        }

        private static void UnhandledException(Exception ex)
        { 
            BlazorWebWindow?.OpenAlertWindowBase("Error", $"{ex.Message}\n{ex.StackTrace}");
        }

        
        internal static async Task AttachToPageAsync(string baseUrl, string startUrl)
        {
            // If there was some previous attached page, dispose all its resources. We're not eagerly disposing
            // page contexts when the user navigates away, because we don't get notified about that. We could
            // change this if any important reason emerges.
            _currentPageContext?.Dispose();

            var serviceScope = _provider.CreateScope();
            _currentPageContext = new PageContext(Dispatcher, serviceScope, _ipcSender, baseUrl, startUrl);

            // Add any root components that were registered before the page attached
            foreach (var (selector, rootComponent) in _rootComponentsBySelector)
            {
                await _currentPageContext.Renderer.AddRootComponentAsync(
                    rootComponent.ComponentType,
                    selector,
                    rootComponent.Parameters);
            }
        }

        //private static async Task RunAsync<TStartup>(IpcSender ipcSender, CancellationToken appLifetime)
        //{
           
        //    var loggerFactory = _provider.GetRequiredService<ILoggerFactory>();

        //    DesktopRenderer = new WebViewRenderer(_provider, Dispatcher,  _ipcSender, loggerFactory, null);

        //}

        private static Stream? SupplyFrameworkFile(string uri)
        {
            switch (uri)
            {
                case "framework://blazor.desktop.js":
                    return typeof(ComponentsDesktop)?.Assembly.GetManifestResourceStream("Photino.Blazor.blazor.desktop.js");
                default:
                    throw new ArgumentException($"Unknown framework file: {uri}");
            }
        }

        //private static async Task PerformHandshakeAsync(IPC ipc)
        //{
        //    var tcs = new TaskCompletionSource<object?>();
        //    ipc.Once("components:init", args =>
        //    {
        //        if (args == null) return;
        //        var argsArray = (object[])args;
        //        InitialUriAbsolute = ((JsonElement)argsArray[0]).GetString();
        //        BaseUriAbsolute = ((JsonElement)argsArray[1]).GetString();

        //        tcs.SetResult(null);
        //    });

        //    await tcs.Task;
        //}

        //private static void AttachJsInterop(IPC ipc, SynchronizationContext synchronizationContext, CancellationToken _)
        //{
        //    ipc.On("BeginInvokeDotNetFromJS", args =>
        //    {
        //        synchronizationContext.Send(state =>
        //        {
        //            if (state == null) return;
        //            var argsArray = (object[])state;

        //            if (argsArray == null || DesktopJSRuntime == null || argsArray[2] is not JsonElement arg2 || argsArray[4] is not JsonElement arg4) return;

        //            DotNetDispatcher.BeginInvokeDotNet(
        //                DesktopJSRuntime,
        //                new DotNetInvocationInfo(
        //                    assemblyName: ((JsonElement)argsArray[1]).GetString(),
        //                    methodIdentifier: arg2.GetString() ?? "",
        //                    dotNetObjectId: ((JsonElement)argsArray[3]).GetInt64(),
        //                    callId: ((JsonElement)argsArray[0]).GetString()),
        //                    arg4.GetString() ?? "");
        //        }, args);
        //    });

        //    ipc.On("EndInvokeJSFromDotNet", args =>
        //    {
        //        synchronizationContext.Send(state =>
        //        {
        //            var argsArray = state as object[];
                  
        //            if (argsArray == null || DesktopJSRuntime == null || argsArray[2] is not JsonElement arg) return;
        //            DotNetDispatcher.EndInvokeJS(
        //                DesktopJSRuntime,
        //                arg.GetString() ?? "");
        //        }, args);
        //    });
        //}

        private static void Log(string message)
        {
            var process = Process.GetCurrentProcess();
            Console.WriteLine($"[{process.ProcessName}:{process.Id}] out: " + message);
        }
    }
}
