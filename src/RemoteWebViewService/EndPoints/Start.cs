using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Identity.Web;
using PeakSWC.RemoteWebView.Pages;
using System.Collections.Concurrent;
using System.Threading.Channels;

namespace PeakSWC.RemoteWebView.EndPoints
{
    public static partial class Endpoints
    {
        public static RequestDelegate Start()
        {
            return async context =>
            {
                // Retrieve the GUID from route values
                string guid = context.Request.RouteValues["id"]?.ToString() ?? string.Empty;
                if (string.IsNullOrEmpty(guid))
                {
                    context.Response.StatusCode = StatusCodes.Status400BadRequest;
                    await context.Response.WriteAsync("Invalid or missing GUID").ConfigureAwait(false);
                    return;
                }

                // Retrieve service state and channels
                var serviceDictionary = context.RequestServices.GetRequiredService<ConcurrentDictionary<string, ServiceState>>();
                var serviceStateChannel = context.RequestServices.GetRequiredService<ConcurrentDictionary<string, Channel<string>>>();

                // Check if the service state exists
                if (!serviceDictionary.TryGetValue(guid, out var serviceState))
                {
                    context.Response.StatusCode = StatusCodes.Status404NotFound;
                    context.Response.ContentType = "text/html";
                    await context.Response.WriteAsync(RestartFailedPage.Html(guid, false)).ConfigureAwait(false);
                    return;
                }

                // Check if the service is already in use
                if (serviceState.InUse)
                {
                    context.Response.StatusCode = StatusCodes.Status409Conflict; // Conflict
                    context.Response.ContentType = "text/html";
                    await context.Response.WriteAsync(LockedPage.Html(serviceState.User, guid)).ConfigureAwait(false);
                    return;
                }

                // Set the service state
                serviceState.Cookies = context.Request.Cookies;
                serviceState.InUse = true;
                serviceState.User = context.User.GetDisplayName() ?? "";

                // Notify that the browser is attached, if applicable
                if (serviceState.IPC.ClientResponseStream != null)
                {
                    await serviceState.IPC.ClientResponseStream
                        .WriteAsync(new WebMessageResponse { Response = "browserAttached:" })
                        .ConfigureAwait(false);
                }

                // Notify all channels of the connection
                foreach (var channel in serviceStateChannel.Values)
                {
                    await channel.Writer.WriteAsync($"Connect:{guid}").ConfigureAwait(false);
                }

                // Redirect to the specific GUID page
                context.Response.Redirect($"/{guid}", permanent: false); // Explicitly non-permanent redirect
            };
        }
    }
}
