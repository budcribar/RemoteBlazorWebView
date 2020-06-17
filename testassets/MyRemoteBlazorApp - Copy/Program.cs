using WebWindows.Blazor;
using System;
using PeakSwc.RemoteableWebWindows;

namespace MyRemoteBlazorApp
{
    public class Program
    {
        static void Main()
        {
            ComponentsDesktop.Run<Startup>(new RemotableWebWindow(new Uri("https://localhost:443"), "My Remote Blazor App", "wwwroot/index.html"));
            
            //ComponentsDesktop.Run<Startup>(new RemotableWebWindow(new Uri("https://webserverstransporter.com:443"), "My Remote Blazor App", "wwwroot/index.html"));

            //ComponentsDesktop.Run<Startup>("My Blazor App", "wwwroot/index.html");
        }
    }
}
