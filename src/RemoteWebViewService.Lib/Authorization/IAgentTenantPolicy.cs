using Microsoft.AspNetCore.Http;

namespace PeakSWC.RemoteWebView
{
    public sealed record AgentEnrollmentDecision(bool Accepted, string Group = "", string? DenialReason = null, string? IssuedAgentToken = null);

    /// <param name="Allowed">Whether the caller may use AgentAdmin RPCs.</param>
    /// <param name="GroupScope">Null means unrestricted/global operator; non-null restricts strictly to agents in that exact group slug.</param>
    /// <param name="UnattendedAuthorized">Whether unattended session starts are authorized for this caller (e.g. valid x-admin-key or authorized member).</param>
    /// <param name="CallerRole">Optional role reported for the authenticated caller.</param>
    public sealed record AgentAdminAccessDecision(bool Allowed, string? GroupScope, bool UnattendedAuthorized, string? DenialReason = null, string? CallerRole = null);

    public interface IAgentTenantPolicy
    {
        AgentEnrollmentDecision CheckEnrollment(string? enrollmentKey, Agents.AgentOptions options);
        AgentEnrollmentDecision CheckEnrollment(string? enrollmentKey, string? machineId, string? agentToken, Agents.AgentOptions options) =>
            CheckEnrollment(enrollmentKey, options);
        AgentAdminAccessDecision CheckAdminAccess(HttpContext? httpContext, string? adminKeyHeader, Agents.AgentOptions options);
    }

    /// <summary>Default policy when no ticket authority is active: delegates directly to <see cref="Agents.AgentOptions"/> with no tenant scoping.</summary>
    public sealed class AllowAllAgentTenantPolicy : IAgentTenantPolicy
    {
        public const string NoKeysConfiguredReason = "This relay has no enrollment keys configured, so it accepts no agents";

        public AgentEnrollmentDecision CheckEnrollment(string? enrollmentKey, Agents.AgentOptions options)
        {
            if (options.IsKeyAccepted(enrollmentKey))
                return new AgentEnrollmentDecision(true, string.Empty);
            var reason = options.RefusesEveryKey ? NoKeysConfiguredReason : "The enrollment key was not accepted";
            return new AgentEnrollmentDecision(false, string.Empty, reason);
        }

        public AgentAdminAccessDecision CheckAdminAccess(HttpContext? httpContext, string? adminKeyHeader, Agents.AgentOptions options) =>
            new(Allowed: true, GroupScope: null, UnattendedAuthorized: options.IsAdminKeyValid(adminKeyHeader));
    }
}
