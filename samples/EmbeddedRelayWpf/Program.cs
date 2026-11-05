using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PeakSWC.RemoteBlazorWebView;
using PeakSWC.RemoteBlazorWebView.Wpf;
using PeakSWC.RemoteWebView;

namespace EmbeddedRelayWpf;

public static class Program
{
    [STAThread]
    public static void Main()
    {
        var app = new Application();

        // 1. Start the relay inside this process: loopback, plain HTTP, free ports, no dev cert.
        var sw = Stopwatch.StartNew();
        EmbeddedRemoteWebViewServer server = RemoteWebViewServer.StartAsync(new RemoteWebViewServerOptions
        {
            MaxClients = 1,
            ConfigureLogging = lb => lb.AddDebug().SetMinimumLevel(LogLevel.Warning),
        }).GetAwaiter().GetResult();
        long startMs = sw.ElapsedMilliseconds;

        // 2. Point a RemoteBlazorWebView at it. ServerUri, GrpcBaseUri and Id must be set before HostPage.
        var services = new ServiceCollection();
        services.AddRemoteWpfBlazorWebView();
        services.AddLogging(lb => lb.AddDebug());

        var id = Guid.NewGuid();
        var webView = new BlazorWebView
        {
            Services = services.BuildServiceProvider(),
            ServerUri = server.BaseUri,
            GrpcBaseUri = server.GrpcUri,
            Id = id,
        };
        webView.RootComponents.Add(new RootComponent { Selector = "#app", ComponentType = typeof(Counter) });
        webView.HostPage = @"wwwroot\index.html";

        var pageUri = new Uri(server.BaseUri, $"app/{id}");
        var status = new TextBlock { Margin = new Thickness(8), TextWrapping = TextWrapping.Wrap };
        status.Text = $"Relay started in {startMs} ms.  Browser: {server.BaseUri}  gRPC: {server.GrpcUri}  Page: {pageUri}";
        webView.Connected += (_, e) => app.Dispatcher.Invoke(() => status.Text += $"\nBrowser connected from {e.IpAddress}.");
        webView.Disconnected += (_, e) => app.Dispatcher.Invoke(() => status.Text += $"\nDisconnected: {e.Exception?.Message}");

        var open = new Button { Content = "Open in browser", Margin = new Thickness(8), Padding = new Thickness(12, 4, 12, 4) };
        open.Click += (_, _) => Process.Start(new ProcessStartInfo(pageUri.ToString()) { UseShellExecute = true });

        var top = new StackPanel { Orientation = Orientation.Horizontal };
        top.Children.Add(open);
        top.Children.Add(status);
        var root = new DockPanel();
        DockPanel.SetDock(top, Dock.Top);
        root.Children.Add(top);
        root.Children.Add(webView);

        var window = new Window { Title = "Embedded relay sample", Width = 1000, Height = 650, Content = root };
        window.Closed += (_, _) => server.StopAsync().GetAwaiter().GetResult();

        app.Run(window);
        server.Dispose();
    }
}
