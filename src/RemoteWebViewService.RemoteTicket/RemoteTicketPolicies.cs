using System.Collections.Concurrent;
using Microsoft.AspNetCore.Http;
using PeakSWC.RemoteWebView.Agents;

namespace PeakSWC.RemoteWebView
{
    public sealed class RemoteTicketSessionPolicy : IShopSessionPolicy
    {
        public static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(30);

        private readonly RemoteTicketAuthority authority;
        private readonly TimeProvider time;
        private readonly TimeSpan cacheFor;
        private readonly bool allowUnregisteredGroups;
        private readonly ConcurrentDictionary<string, CacheEntry> cache = new(StringComparer.OrdinalIgnoreCase);

        public RemoteTicketSessionPolicy(HttpClient http, string secret, TimeProvider time, bool allowUnregisteredGroups)
            : this(http, secret, time, CacheDuration, allowUnregisteredGroups)
        {
        }

        public RemoteTicketSessionPolicy(HttpClient http, string secret, TimeProvider time, TimeSpan cacheFor, bool allowUnregisteredGroups)
        {
            authority = new RemoteTicketAuthority(http, secret);
            this.time = time;
            this.cacheFor = cacheFor;
            this.allowUnregisteredGroups = allowUnregisteredGroups;
        }

        public async Task<SessionGateDecision> CheckCreateSessionAsync(string group, int activeInGroupCount, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(group))
                return new SessionGateDecision(false, "group_inactive", "A group is required.");

            var lookup = await LookupAsync(group, ct).ConfigureAwait(false);
            if (!lookup.Reachable)
                return new SessionGateDecision(false, "authority_unreachable", "The relay authority could not be reached.");
            if (lookup.Info == null || !lookup.Info.Active)
            {
                if (allowUnregisteredGroups)
                    return new SessionGateDecision(true);
                return new SessionGateDecision(false, "group_inactive", "This group is not active.");
            }

            if (activeInGroupCount >= lookup.Info.MaxConcurrentSessions)
                return new SessionGateDecision(false, "session_limit_reached", "This group is at its concurrent remote session limit.");
            return new SessionGateDecision(true);
        }

        private async Task<RemoteTicketGroupLookup> LookupAsync(string group, CancellationToken cancellationToken)
        {
            var now = time.GetUtcNow();
            if (cache.TryGetValue(group, out var cached) && cached.Expires > now)
                return cached.Lookup;

            var lookup = await authority.GetGroupAsync(group, cancellationToken).ConfigureAwait(false);
            if (lookup.Reachable && lookup.Info != null)
                cache[group] = new CacheEntry(lookup, now.Add(cacheFor));
            return lookup;
        }

        private readonly record struct CacheEntry(RemoteTicketGroupLookup Lookup, DateTimeOffset Expires);
    }

    public sealed class RemoteTicketAgentPolicy : IAgentTenantPolicy
    {
        private readonly RemoteTicketAuthority authority;

        public RemoteTicketAgentPolicy(HttpClient http, string secret) =>
            authority = new RemoteTicketAuthority(http, secret);

        public AgentEnrollmentDecision CheckEnrollment(string? enrollmentKey, AgentOptions options)
        {
            var result = authority.AuthorizeAsync(enrollmentKey, null, CancellationToken.None).GetAwaiter().GetResult();
            return result.Accepted
                ? new AgentEnrollmentDecision(true, result.Group ?? "")
                : new AgentEnrollmentDecision(false, "", result.DenialReason ?? "The enrollment key was not accepted");
        }

        public AgentAdminAccessDecision CheckAdminAccess(HttpContext? httpContext, string? adminKeyHeader, AgentOptions options)
        {
            var ticket = RemoteTicketReader.From(httpContext);
            if (string.IsNullOrEmpty(ticket))
                return new(Allowed: false, GroupScope: "", UnattendedAuthorized: false, DenialReason: "Sign in to continue.");

            var result = authority.AuthorizeAsync(null, ticket, CancellationToken.None).GetAwaiter().GetResult();
            if (!result.Accepted)
                return new(Allowed: false, GroupScope: result.Group ?? "", UnattendedAuthorized: false, DenialReason: result.DenialReason ?? "Access denied");
            var scope = string.IsNullOrEmpty(result.Group) ? null : result.Group;
            return new(Allowed: true, GroupScope: scope, UnattendedAuthorized: true);
        }
    }

    public static class RemoteTicketReader
    {
        public const string CookieName = "rwv-relay-ticket";
        public const string HeaderName = "x-relay-ticket";

        public static string? From(HttpContext? http)
        {
            if (http == null)
                return null;
            var header = http.Request.Headers[HeaderName].ToString();
            if (!string.IsNullOrWhiteSpace(header))
                return header.Trim();
            return http.Request.Cookies.TryGetValue(CookieName, out var cookie) ? cookie : null;
        }
    }
}
