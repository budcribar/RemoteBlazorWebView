using System;
using System.Collections.Generic;
using System.Reflection;
using Xunit;

namespace FileSyncServer.Tests
{
    /// <summary>
    /// Which addresses load inside the control's own WebView and which open the system browser. The class is compiled into both the
    /// WPF and the WinForms control (same name, same namespace), so it is reached through each control's assembly.
    /// </summary>
    public class UrlLoadingStrategyTests
    {
        private const string Id = "0f8fad5b-d9cb-469f-a165-70867728950e";
        private const string Relay = "https://relay.example:5001";

        public static IEnumerable<object[]> Controls()
        {
            yield return new object[] { typeof(PeakSWC.RemoteBlazorWebView.Wpf.BlazorWebView) };
            yield return new object[] { typeof(PeakSWC.RemoteBlazorWebView.WindowsForms.BlazorWebView) };
        }

        private static string Strategy(Type control, string url)
        {
            var type = control.Assembly.GetType("PeakSWC.RemoteBlazorWebView.UrlLoadingEventArgs", throwOnError: true)!;
            var create = type.GetMethod("CreateWithDefaultLoadingStrategy", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!;
            var args = create.Invoke(null, new object[] { new Uri(url), new Uri("https://0.0.0.0/") })!;
            return type.GetProperty("UrlLoadingStrategy")!.GetValue(args)!.ToString()!;
        }

        [Theory]
        [MemberData(nameof(Controls))]
        public void The_apps_own_pages_and_the_session_pages_load_in_the_WebView(Type control)
        {
            foreach (var url in new[]
            {
                "https://0.0.0.0/",
                "https://0.0.0.0/counter",
                $"{Relay}/mirror/{Id}",
                $"{Relay}/app/{Id}?role=owner",                 // the owner's own surface
                $"{Relay}/app/{Id}?role=owner&control=remote",  // unattended
                $"{Relay}/{Id}",                                // where /app redirects to
                $"{Relay}/{Id}?role=owner",
                $"{Relay}/{Id}/counter",
                "data:text/html,hello",
            })
                Assert.True(Strategy(control, url) == "OpenInWebView", $"{url} should load in the WebView");
        }

        [Theory]
        [MemberData(nameof(Controls))]
        public void Everything_else_opens_in_the_system_browser(Type control)
        {
            foreach (var url in new[]
            {
                $"{Relay}/app/{Id}",                            // the "open in your browser" link an app shows
                $"{Relay}/app/{Id}?theme=dark",
                $"{Relay}/app/not-a-guid?role=owner",
                $"{Relay}/mirror/not-a-guid",
                $"{Relay}/docs/page",
                "https://example.com/",
            })
                Assert.True(Strategy(control, url) == "OpenExternally", $"{url} should open in the system browser");
        }
    }
}
