using Microsoft.Extensions.DependencyInjection;
using MyRemoteBlazorAppCopy;
using WebWindows.Blazor;

namespace MyRemoteBlazorApp
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
