using PeakSWC.RemoteBlazorWebView.Windows;

using System;

namespace HelloRemotePhotino.Blazor
{
    public class Program
    {
        [STAThread]
        static void Main(string[] _)
        {
            //var pw = new PhotinoWindow("Hello Remote Photino Blazor!");
            //var pw = new RemotePhotinoWindow(null, "wwwroot/index.html", "Hello Remote Photino Blazor!", default, (o) => { o.WebMessageReceivedHandler = (s, e) => { Console.WriteLine($"Web Message {e}"); }; });
            //var pw = new RemoteBlazorWebWindow(new Uri("https://localhost:443"), "wwwroot/index.html", "Hello Remote Photino Blazor!", new Guid("BC7D925A-2F19-495A-A41E-DDC3C0187071"), (o) => { o.WebMessageReceivedHandler = (s, e) => { Console.WriteLine($"Web Message {e}"); }; });

            var pw = new RemoteBlazorWebWindow(new Uri("https://localhost:443"), "wwwroot/index.html", "Hello Remote Photino Blazor!", default);
            ComponentsDesktop.Run<Startup>(pw);

            //var pw = new RemotePhotinoWindow(new Uri("https://webserverstransporter.com:443"), "wwwroot/index.html", "Hello Remote Photino Blazor!", default, (o)=> { o.WebMessageReceivedHandler = (s, e) => { Console.WriteLine($"Web Message {e}"); }; });

            //ComponentsDesktop.Run<Startup>("Hello Remote Photino Blazor!", "wwwroot/index.html");
        }
    }
}
