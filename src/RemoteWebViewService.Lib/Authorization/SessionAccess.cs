using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using PeakSWC.RemoteWebView.Services;
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;

namespace PeakSWC.RemoteWebView
{
    /// <summary>
    /// Who may open a session's pages (/app, /mirror, /{id}, /status, /wait) when the relay requires sign-in
    /// (any <see cref="IRelayAuthorization"/> but <see cref="NoAuthorization"/>):
    /// <list type="bullet">
    /// <item>the app's own owner page, without signing in: /app/{id}?role=owner&amp;key=... with the app's owner key, then
    /// the owner cookie (OwnerAccess). A customer's PC has no account to sign in with.</item>
    /// <item>a signed-in user in the session's group - the same groups that decide which clients the admin list shows.
    /// Being signed in is not enough on its own: a forwarded /mirror link must not open someone else's session.</item>
    /// </list>
    /// Endpoints without a session id (/stats, /grpcbaseuri) need only a signed-in user.
    /// </summary>
    public sealed class SessionAccessRequirement : IAuthorizationRequirement
    {
        public const string PolicyName = "RemoteSession";
    }

    public sealed class SessionAccessHandler(
        ConcurrentDictionary<string, TaskCompletionSource<ServiceState>> sessions,
        IUserService users) : AuthorizationHandler<SessionAccessRequirement>
    {
        // The app opens its owner page as soon as the relay answers "created:", which can be a moment before the session
        // is published; wait a little for it rather than send the owner page to a sign-in.
        private static readonly TimeSpan SessionWait = TimeSpan.FromSeconds(5);

        protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, SessionAccessRequirement requirement)
        {
            var http = context.Resource as HttpContext;
            var id = http?.GetRouteValue("id")?.ToString();
            var state = id is null ? null : await TryGetSession(id).ConfigureAwait(false);

            if (http is not null && state is not null && IsOwnerPage(http, state))
            {
                context.Succeed(requirement);
                return;
            }

            if (context.User.Identity?.IsAuthenticated != true)
                return; // not signed in: the policy challenges (sign-in page)

            if (state is null)
            {
                context.Succeed(requirement); // no session here (or not yet): signed in is enough
                return;
            }

            var groups = await users.GetUserGroups(context.User.GetUserObjectId() ?? string.Empty).ConfigureAwait(false) ?? [];
            if (groups.Contains(state.Group))
                context.Succeed(requirement);
            // otherwise not succeeded: a signed-in user outside the session's group is refused (403)
        }

        private static bool IsOwnerPage(HttpContext http, ServiceState state) =>
            OwnerAccess.HasOwnerCookie(http, state) ||
            (http.Request.Query["role"] == "owner" && OwnerAccess.KeyMatches(state, http.Request.Query["key"]));

        private async Task<ServiceState?> TryGetSession(string id)
        {
            if (!sessions.TryGetValue(id, out var source))
                return null;
            try
            {
                return await source.Task.WaitAsync(SessionWait).ConfigureAwait(false);
            }
            catch (Exception)
            {
                return null; // not published in time, or failed
            }
        }
    }
}
