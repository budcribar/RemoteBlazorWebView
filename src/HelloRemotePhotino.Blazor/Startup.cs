using Microsoft.Extensions.DependencyInjection;
using PeakSWC.RemoteBlazorWebView.Windows;

namespace HelloRemotePhotino.Blazor
{
    public class Startup
    {
        public void ConfigureServices(IServiceCollection services)
        {
        }

        public void Configure(DesktopApplicationBuilder app)
        {
            app.AddComponent<App>("app");
        }
    }
}
