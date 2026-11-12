using Microsoft.AspNetCore.Http;

namespace PeakSWC.RemoteWebView
{
    public sealed record AgentEnrollmentDecision(bool Accepted, string Group = "", string? DenialReason = null);

    /// <param name="Allowed">Whether the caller may use AgentAdmin RPCs.</param>
    /// <param name="GroupScope">Null means unrestricted/global operator; non-null restricts strictly to agents in that exact group slug.</param>
    /// <param name="UnattendedAuthorized">Whether unattended session starts are authorized for this caller (e.g. valid x-admin-key or authenticated MSP member).</param>
    public sealed record AgentAdminAccessDecision(bool Allowed, string? GroupScope, bool UnattendedAuthorized, string? DenialReason = null);

    public interface IAgentTenantPolicy
    {
        AgentEnrollmentDecision CheckEnrollment(string? enrollmentKey, Agents.AgentOptions options);
        AgentAdminAccessDecision CheckAdminAccess(HttpContext? httpContext, string? adminKeyHeader, Agents.AgentOptions options);
    }

    /// <summary>Default policy when no ticket authority is active: delegates directly to <see cref="Agents.AgentOptions"/> with no tenant scoping.</summary>
    public sealed class AllowAllAgentTenantPolicy : IAgentTenantPolicy
    {
        public AgentEnrollmentDecision CheckEnrollment(string? enrollmentKey, Agents.AgentOptions options) =>
            options.IsKeyAccepted(enrollmentKey)
                ? new AgentEnrollmentDecision(true, string.Empty)
                : new AgentEnrollmentDecision(false, string.Empty, "The enrollment key was not accepted");

        public AgentAdminAccessDecision CheckAdminAccess(HttpContext? httpContext, string? adminKeyHeader, Agents.AgentOptions options) =>
            new(Allowed: true, GroupScope: null, UnattendedAuthorized: options.IsAdminKeyValid(adminKeyHeader));
    }
}
