using Grpc.Core;
using Microsoft.AspNetCore.Http;
using PeakSWC.RemoteWebView.Agents;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace PeakSWC.RemoteWebView
{
    /// <summary>
    /// Who may use a session through the browser calls (BrowserIPC) when the relay requires sign-in: the same people who
    /// may open its pages (<see cref="SessionAccessHandler"/>). The pages' gRPC-Web calls go to the relay that served them,
    /// so they carry the same sign-in (and owner) cookies.
    /// <list type="bullet">
    /// <item>the app's own owner page: the owner token on the stream, the owner cookie, or the owner's own connection
    /// (its clientId with its key).</item>
    /// <item>a signed-in user in the session's group.</item>
    /// </list>
    /// A relay without sign-in lets every call through, as before.
    /// </summary>
    public static class BrowserCallAccess
    {
        public static async Task<bool> AllowsAsync(ServerCallContext? context, ServiceState state, IRelayAuthorization? authorization, IUserService? users,
            string? ownerToken = null, string? clientId = null, string? clientKey = null)
        {
            if (authorization?.Enabled != true)
                return true;

            if (OwnerAccess.TokenMatches(state, ownerToken))
                return true;
            if (!string.IsNullOrEmpty(clientId) && clientId == state.OwnerClientId && state.ClientKeys.Matches(clientId, clientKey))
                return true;

            var http = CallContexts.Http(context);
            if (http is null)
                return false;
            if (OwnerAccess.HasOwnerCookie(http, state))
                return true;
            return users is not null && await SessionMembership.IsMemberAsync(http.User, state, users).ConfigureAwait(false);
        }
    }

    /// <summary>A signed-in user may use a session when they are in its group: the rule for its pages and its gRPC calls.</summary>
    public static class SessionMembership
    {
        public static async Task<bool> IsMemberAsync(ClaimsPrincipal? user, ServiceState state, IUserService users)
        {
            if (user?.Identity?.IsAuthenticated != true)
                return false;
            var groups = await users.GetUserGroups(user.GetUserObjectId() ?? string.Empty).ConfigureAwait(false) ?? [];
            return groups.Contains(state.Group);
        }

        /// <summary>
        /// HTTP session access check used after a session resolves (for example after waiting on a pre-opened
        /// /app/{id} or /mirror/{id} URL, and for static files served by RemoteFilesMiddleware).
        /// </summary>
        public static async Task<bool> AllowsHttpAsync(HttpContext http, ServiceState state, IRelayAuthorization? authorization, IUserService? users)
        {
            if (authorization?.Enabled != true)
                return true;
            if (OwnerAccess.HasOwnerCookie(http, state) ||
                (http.Request.Query["role"] == "owner" && OwnerAccess.KeyMatches(state, http.Request.Query["key"])))
                return true;
            return users is not null && await IsMemberAsync(http.User, state, users).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Strips relay and framework authentication cookies before forwarding browser cookies to the desktop client.
    /// The desktop client runs on a customer machine and must never receive a technician's relay ticket, ASP.NET
    /// auth cookies, owner tokens, or voice tokens.
    /// </summary>
    public static class RelayCookieFilter
    {
        public static bool IsSensitive(string name) =>
            string.IsNullOrEmpty(name) ||
            name.Equals("rwv-relay-ticket", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("rbwv-test-user", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("rbwv-owner-", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("rbwv-voice-", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith(".AspNetCore.", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("__Host-", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("__Secure-", StringComparison.OrdinalIgnoreCase);

        public static Dictionary<string, string> FilterForClient(IRequestCookieCollection? cookies)
        {
            var filtered = new Dictionary<string, string>(StringComparer.Ordinal);
            if (cookies is null)
                return filtered;
            foreach (var c in cookies)
            {
                if (!IsSensitive(c.Key))
                    filtered[c.Key] = c.Value;
            }
            return filtered;
        }
    }

    /// <summary>
    /// What a caller of the relay-wide calls (ClientIPC GetServerStatus, GetLoggedEvents, SetCache) may see.
    /// <see cref="All"/>: an operator, or any caller on a relay without sign-in. Otherwise only the sessions of
    /// <see cref="Groups"/>, and nothing when that is empty.
    /// </summary>
    public sealed record RelayCallerScope(bool All, IReadOnlyCollection<string> Groups)
    {
        public static readonly RelayCallerScope Everything = new(true, []);
        public static readonly RelayCallerScope Nobody = new(false, []);

        public bool IsOperator => All;
        public bool IsNobody => !All && Groups.Count == 0;
        public bool Sees(string group) => All || Groups.Contains(group);

        /// <summary>
        /// The operator check is the one AgentAdmin uses (<see cref="IAgentTenantPolicy.CheckAdminAccessAsync"/>): allowed with
        /// no group scope and authorized for unattended starts (a relay ticket for every shop, or a valid x-admin-key).
        /// A shop-scoped ticket sees its own shop; any other signed-in user sees the groups they are in.
        /// </summary>
        public static async Task<RelayCallerScope> ResolveAsync(ServerCallContext? context, IRelayAuthorization? authorization,
            IAgentTenantPolicy? policy, AgentOptions? options, IUserService? users)
        {
            if (authorization?.Enabled != true)
                return Everything;

            var http = CallContexts.Http(context);
            if (policy is not null && options is not null)
            {
                var access = await policy.CheckAdminAccessAsync(http, CallContexts.Header(context, AgentOptions.AdminKeyHeader), options, context?.CancellationToken ?? default).ConfigureAwait(false);
                if (access.Allowed && access.GroupScope is null && access.UnattendedAuthorized)
                    return Everything;
                if (access.Allowed && !string.IsNullOrEmpty(access.GroupScope))
                    return new RelayCallerScope(false, [access.GroupScope]);
            }

            var user = http?.User;
            if (user?.Identity?.IsAuthenticated != true || users is null)
                return Nobody;
            var groups = await users.GetUserGroups(user.GetUserObjectId() ?? string.Empty).ConfigureAwait(false) ?? [];
            return new RelayCallerScope(false, groups.ToArray());
        }
    }

    /// <summary>
    /// The app's own calls (WebViewIPC SendMessage, Shutdown, Ping, RequestClientFileRead). The app picks a secret and
    /// sends it with CreateWebView (appKey); its later calls carry it in the <see cref="HeaderName"/> header. A session
    /// whose app sent no key (older apps) accepts the id alone, as before.
    /// </summary>
    public static class AppAccess
    {
        public const string HeaderName = "x-app-key";

        public static bool IsKeyed(ServiceState state) => !string.IsNullOrEmpty(state.AppKey);

        public static bool Allows(ServiceState state, ServerCallContext? context) =>
            !IsKeyed(state) || FixedEquals(state.AppKey, CallContexts.Header(context, HeaderName));

        private static bool FixedEquals(string expected, string? actual)
        {
            if (string.IsNullOrEmpty(actual))
                return false;
            var a = Encoding.UTF8.GetBytes(expected);
            var b = Encoding.UTF8.GetBytes(actual);
            return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
        }
    }

    internal static class CallContexts
    {
        /// <summary>The call's HttpContext, or null when it has none (tests that make their own ServerCallContext).</summary>
        public static HttpContext? Http(ServerCallContext? context)
        {
            if (context is null)
                return null;
            try
            {
                return context.GetHttpContext();
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }

        public static string? Header(ServerCallContext? context, string name)
        {
            if (context?.RequestHeaders is not { } headers)
                return null;
            foreach (var entry in headers)
            {
                if (!entry.IsBinary && string.Equals(entry.Key, name, StringComparison.OrdinalIgnoreCase))
                    return entry.Value;
            }
            return null;
        }
    }
}
