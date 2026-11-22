using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using PeakSWC.RemoteWebView.Pages;
using System;
using System.Collections.Concurrent;
using System.Linq;
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
                using var pending = PendingSessions.Join(serviceDictionary, guid);
                var serviceStateTaskSource = pending.Source;
                var serviceStateChannel = context.RequestServices.GetRequiredService<ConcurrentDictionary<string, Channel<string>>>();

                ServiceState? serviceState = null;
                bool markedInUse = false;
                try
                {
                    serviceState = await serviceStateTaskSource.Task.WaitAsync(TimeSpan.FromSeconds(60)).ConfigureAwait(false);

                    // Re-verify session access after waiting: SessionAccessHandler lets an authenticated user through
                    // when the session had not registered yet (pre-opened URL), so verify group membership now.
                    var authorization = context.RequestServices.GetService<IRelayAuthorization>();
                    if (authorization?.Enabled == true)
                    {
                        var users = context.RequestServices.GetService<IUserService>();
                        if (!await SessionMembership.AllowsHttpAsync(context, serviceState, authorization, users).ConfigureAwait(false))
                        {
                            LogPageRequest(context, guid, serviceState, "refused (not a member of this session's group)");
                            context.Response.StatusCode = context.User.Identity?.IsAuthenticated == true
                                ? StatusCodes.Status403Forbidden
                                : StatusCodes.Status401Unauthorized;
                            return;
                        }
                    }

                    // The owner's page: when the app sent an owner key, only the page the app opened with that key is the owner.
                    // A wrong or missing key is refused before it can take the /app page.
                    bool ownerPage = context.Request.Query["role"] == "owner";
                    if (ownerPage && OwnerAccess.IsKeyed(serviceState) && !OwnerAccess.KeyMatches(serviceState, context.Request.Query["key"]))
                    {
                        LogPageRequest(context, guid, serviceState, "refused (owner page without the app's key)");
                        context.Response.StatusCode = StatusCodes.Status403Forbidden;
                        context.Response.ContentType = "text/plain";
                        await context.Response.WriteAsync("This link cannot open the owner's page.").ConfigureAwait(false);
                        return;
                    }

                    // A shared session's /app page belongs to the owner's surface. Anyone else joins at the mirror, which can never
                    // restart the app: after a reboot, a remote browser coming back here must not take the page from the app's
                    // own window (and its later loads of /{id} must not count as refreshes). Unattended control still reaches
                    // them there: the owner's page hands control to whoever joins.
                    if (SendsToMirror(serviceState, context))
                    {
                        LogPageRequest(context, guid, serviceState, "sent to /mirror (no owner key or cookie)");
                        RedirectToMirror(context, guid);
                        return;
                    }

                    // Check if the service is already in use. The owner's own surface, proven by the app's key (checked above), is
                    // never locked out: a remote browser that reached /app first (a tab left open from before the app was
                    // restarted, say) must not keep the person at the PC out of their own window, where control is given and
                    // taken back.
                    bool keyedOwner = ownerPage && OwnerAccess.IsKeyed(serviceState);
                    bool alreadyInUse = serviceState.InUse;
                    if (alreadyInUse && !keyedOwner)
                    {
                        LogPageRequest(context, guid, serviceState, $"locked (in use by {serviceState.User})");
                        context.Response.StatusCode = StatusCodes.Status409Conflict; // Conflict
                        context.Response.ContentType = "text/html";
                        await context.Response.WriteAsync(LockedPage.Html(serviceState.User, guid)).ConfigureAwait(false);
                        return;
                    }
                    // Notify that the browser is attached, if applicable
                    await serviceState.IPC.WriteToClientAsync(new WebMessageResponse { Response = "browserAttached:" }).ConfigureAwait(false);

                    // Set the service state only after notifying the client stream succeeds. An owner coming in after another
                    // browser leaves that browser's cookies and name in place: it is still on the session.
                    if (!alreadyInUse)
                    {
                        serviceState.Cookies = context.Request.Cookies;
                        serviceState.InUse = true;
                        markedInUse = true;
                        serviceState.User = context.User.GetUserDisplayName() ?? "";
                    }

                    // Notify all channels of the connection
                    foreach (var channel in serviceStateChannel.Values)
                    {
                        channel.Writer.TryWrite($"Connect:{guid}");
                    }

                    // The remote user's end of the voice call goes to the remote user's page. When the owner's own surface takes
                    // /app (it already has the host end), the remote user joins at /mirror and gets it there instead.
                    if (ownerPage)
                    {
                        serviceState.OwnerHoldsApp = true;
                        if (OwnerAccess.IsKeyed(serviceState))
                            OwnerAccess.SetOwnerCookie(context, serviceState);
                    }
                    else
                        serviceState.Voice?.GrantController(context, guid);

                    // Redirect to the specific GUID page, keeping the query (e.g. ?role=owner, read by the page's script)
                    // but not the owner key: from here on the owner cookie stands for it.
                    var query = context.Request.Query.ContainsKey("key")
                        ? QueryString.Create(context.Request.Query.Where(q => q.Key != "key")).ToString()
                        : context.Request.QueryString.ToString();
                    LogPageRequest(context, guid, serviceState, ownerPage ? $"owner took the app page; redirected to /{guid}" : $"took the app page; redirected to /{guid}");
                    context.Response.Redirect($"/{guid}{query}", permanent: false);
                }
               
                catch(Exception)
                {
                    LogPageRequest(context, guid, serviceState, serviceState == null ? "no app registered this id in time" : "failed");
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
