using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Identity.Web;
using PeakSWC.RemoteWebView.Pages;
using System.Collections.Concurrent;
using System.Reflection;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace PeakSWC.RemoteWebView.EndPoints
{
    public static partial class Endpoints
    {
        public static RequestDelegate Wait()
        {
           
            return async context =>
            {
                string guid = context.Request.RouteValues["id"]?.ToString() ?? string.Empty;
                var serviceDictionary = context.RequestServices.GetRequiredService<ConcurrentDictionary<string, ServiceState>>();

                for (int i = 0; i < 30; i++)
                {
                    if (serviceDictionary.ContainsKey(guid))
                    {
                        await context.Response.WriteAsync($"Wait completed").ConfigureAwait(false);
                        return;
                    }

                    await Task.Delay(1000).ConfigureAwait(false);
                }

                context.Response.StatusCode = 400;
                context.Response.ContentType = "text/html";
                await context.Response.WriteAsync(RestartFailedPage.Fragment(guid)).ConfigureAwait(false);
            };
        }

    }
}
