using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Identity.Web;
using PeakSWC.RemoteWebView.Pages;
using System.Collections.Concurrent;
using System.Reflection;
using System.Threading.Channels;

namespace PeakSWC.RemoteWebView.EndPoints
{
    public static partial class Endpoints
    {
        public static RequestDelegate Start()
        {
            return async context =>
            {
                string guid = context.Request.RouteValues["id"]?.ToString() ?? string.Empty;
                var serviceDictionary = context.RequestServices.GetRequiredService<ConcurrentDictionary<string, ServiceState>>();
                var serviceStateChannel = context.RequestServices.GetRequiredService<ConcurrentDictionary<string, Channel<string>>>();

                if (serviceDictionary.TryGetValue(guid, out var serviceState))
                {
                    if (serviceState.InUse)
                    {
                        context.Response.StatusCode = 400;
                        context.Response.ContentType = "text/html";
                        await context.Response.WriteAsync(LockedPage.Html(serviceState.User, guid)).ConfigureAwait(false);
                    }
                    else
                    {
                        serviceState.Cookies = context.Request.Cookies;
                        serviceState.InUse = true;
                        serviceState.User = context.User.GetDisplayName() ?? "";

                        if (serviceState.IPC.ClientResponseStream != null)
                            await serviceState.IPC.ClientResponseStream.WriteAsync(new WebMessageResponse { Response = "browserAttached:" }).ConfigureAwait(false);
                        // Update Status
                        foreach (var channel in serviceStateChannel.Values)
                            await channel.Writer.WriteAsync($"Connect:{guid}").ConfigureAwait(false);

                        context.Response.Redirect($"/{guid}");
                    }
                }
                else
                {
                    context.Response.StatusCode = 400;
                    context.Response.ContentType = "text/html";
                    await context.Response.WriteAsync(RestartFailedPage.Html(guid, false)).ConfigureAwait(false);
                }
            };
        }

    }
}
