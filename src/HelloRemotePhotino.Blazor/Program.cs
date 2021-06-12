using PeakSWC.RemotePhotinoNET;
using Photino.Blazor;
using PhotinoNET;
using System;

namespace HelloRemotePhotino.Blazor
{
    public class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            //var pw = new PhotinoWindow("Hello Remote Photino Blazor!");
            //var pw = new RemotePhotinoWindow(null, "wwwroot/index.html", "Hello Remote Photino Blazor!", default, (o) => { o.WebMessageReceivedHandler = (s, e) => { Console.WriteLine($"Web Message {e}"); }; });

            var pw = new RemotePhotinoWindow(new Uri("https://localhost:443"), "wwwroot/index.html", "Hello Remote Photino Blazor!", default, (o) => { o.WebMessageReceivedHandler = (s, e) => { Console.WriteLine($"Web Message {e}"); }; });
            ComponentsDesktop.Run<Startup>(pw);

            //var pw = new RemotePhotinoWindow(new Uri("https://webserverstransporter.com:443"), "wwwroot/index.html", "Hello Remote Photino Blazor!", default, (o)=> { o.WebMessageReceivedHandler = (s, e) => { Console.WriteLine($"Web Message {e}"); }; });

            //ComponentsDesktop.Run<Startup>("Hello Remote Photino Blazor!", "wwwroot/index.html");
        }
    }
}
