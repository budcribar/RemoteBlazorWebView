using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using PeakSWC.RemoteWebView.Pages;
using System;
using System.Collections.Concurrent;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace PeakSWC.RemoteWebView.EndPoints
{
    public static partial class Endpoints
    {
        public static RequestDelegate Start()
        {
            return async context =>
            {
                var client = await TryReadClientAsync(context).ConfigureAwait(false);
                if (client == null)
                    return;

                var (parsedGuid, serviceDictionary) = client.Value;
                string guid = parsedGuid.ToString();
                var serviceStateTaskSource = serviceDictionary.GetOrAdd(guid, _ => new TaskCompletionSource<ServiceState>(TaskCreationOptions.RunContinuationsAsynchronously));
                var serviceStateChannel = context.RequestServices.GetRequiredService<ConcurrentDictionary<string, Channel<string>>>();

                ServiceState? serviceState = null;
                bool markedInUse = false;
                try
                {
                    serviceState = await serviceStateTaskSource.Task.WaitAsync(TimeSpan.FromSeconds(60)).ConfigureAwait(false);
                    // Check if the service is already in use
                    if (serviceState.InUse)
                    {
                        context.Response.StatusCode = StatusCodes.Status409Conflict; // Conflict
                        context.Response.ContentType = "text/html";
                        await context.Response.WriteAsync(LockedPage.Html(serviceState.User, guid)).ConfigureAwait(false);
                        return;
                    }
                    // Notify that the browser is attached, if applicable
                    await serviceState.IPC.WriteToClientAsync(new WebMessageResponse { Response = "browserAttached:" }).ConfigureAwait(false);

                    // Set the service state only after notifying the client stream succeeds
                    serviceState.Cookies = context.Request.Cookies;
                    serviceState.InUse = true;
                    markedInUse = true;
                    serviceState.User = context.User.GetUserDisplayName() ?? "";

                    // Notify all channels of the connection
                    foreach (var channel in serviceStateChannel.Values)
                    {
                        channel.Writer.TryWrite($"Connect:{guid}");
                    }

                    // The remote user's end of the voice call goes to the remote user's page. When the owner's own surface takes
                    // /app (it already has the host end), the remote user joins at /mirror and gets it there instead.
                    if (context.Request.Query["role"] == "owner")
                        serviceState.OwnerHoldsApp = true;
                    else
                        serviceState.Voice?.GrantController(context, guid);

                    // Redirect to the specific GUID page, keeping the query (e.g. ?role=owner, read by the page's script).
                    context.Response.Redirect($"/{guid}{context.Request.QueryString}", permanent: false);
                }
               
                catch(Exception)
                {
                    if (markedInUse && serviceState != null)
                    {
                        serviceState.InUse = false;
                        serviceState.OwnerHoldsApp = false;
                        serviceState.Cookies = null;
                        serviceState.User = "";
                    }
                    if (!context.Response.HasStarted)
                    {
                        context.Response.StatusCode = StatusCodes.Status404NotFound;
                        context.Response.ContentType = "text/html";
                        await context.Response.WriteAsync(RestartFailedPage.Html(guid, false)).ConfigureAwait(false);
                    }
                    return;
                }
               
            };
        }
    }
}
