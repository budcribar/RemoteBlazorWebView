using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Linq;

namespace PeakSWC.RemoteWebView.EndPoints
{
    public static partial class Endpoints
    {
        /// <summary>
        /// In a shared session (the app sent an owner key), only the owner's own surface may have the app's page: a browser
        /// that asks for /app/{id} or /{id} without the owner key or cookie is sent to /mirror/{id}. A mirror can never be
        /// taken for a refresh, so it cannot restart the app. Sessions without an owner key (older apps, remote components)
        /// and sessions without mirrors keep the old behaviour.
        /// </summary>
        internal static bool SendsToMirror(ServiceState state, HttpContext context) =>
            OwnerAccess.IsKeyed(state) && state.EnableMirrors && !IsOwnerRequest(state, context);

        private static bool IsOwnerRequest(ServiceState state, HttpContext context) =>
            OwnerAccess.HasOwnerCookie(context, state) ||
            (context.Request.Query["role"] == "owner" && OwnerAccess.KeyMatches(state, context.Request.Query["key"]));

        /// <summary>Redirects to the session's mirror page, keeping the query but never the owner key.</summary>
        internal static void RedirectToMirror(HttpContext context, string id)
        {
            var query = context.Request.Query.ContainsKey("key")
                ? QueryString.Create(context.Request.Query.Where(q => q.Key != "key")).ToString()
                : context.Request.QueryString.ToString();
            context.Response.Redirect($"/mirror/{id}{query}", permanent: false);
        }

        /// <summary>
        /// One Information line per page request on /app/{id}, /{id} and /mirror/{id}: what was asked for, by whom, and what the
        /// relay did with it. Startup lets this category through in production, so the browser that restarted an app can be
        /// found in the relay log. The query is left out: it can hold the owner key.
        /// </summary>
        internal static void LogPageRequest(HttpContext context, string id, ServiceState? state, string decision)
        {
            var logger = context.RequestServices.GetService<ILogger<RemoteWebViewService>>();
            if (logger is null || !logger.IsEnabled(LogLevel.Information))
                return;

            string user = context.User?.GetUserDisplayName() ?? "(not signed in)";
            string ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            string forwarded = context.Request.Headers["X-Forwarded-For"].ToString();
            if (!string.IsNullOrEmpty(forwarded))
                ip = $"{forwarded} (via {ip})";

            logger.LogInformation("Page {Path} for session {Id}: {Decision}. User: {User}, IP: {Ip}, owner proof: {OwnerProof}",
                context.Request.Path.Value, id, decision, user, ip, OwnerProof(context, state));
        }

        private static string OwnerProof(HttpContext context, ServiceState? state)
        {
            if (state is null)
                return "unknown (no session)";
            if (!OwnerAccess.IsKeyed(state))
                return "not required (no owner key)";
            if (OwnerAccess.HasOwnerCookie(context, state))
                return "owner cookie";
            string? key = context.Request.Query["key"];
            if (string.IsNullOrEmpty(key))
                return "none";
            return OwnerAccess.KeyMatches(state, key) ? "owner key" : "wrong owner key";
        }
    }
}
