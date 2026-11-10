using PeakSWC.RemoteWebView.Agent;

namespace PeakSWC.RemoteWebView.Agents
{
    /// <summary>The outcome of <see cref="ConsentPolicy.Decide"/>: either a refusal (<see cref="Error"/>) or whether to ask.</summary>
    [System.Diagnostics.CodeAnalysis.Experimental(ExperimentalApi.AgentRegistry, UrlFormat = ExperimentalApi.UrlFormat)]
    public readonly record struct StartDecision(bool Allowed, bool RequireConsent, string? Error)
    {
        public static StartDecision Refuse(string error) => new(false, true, error);
        public static StartDecision Allow(bool requireConsent) => new(true, requireConsent, null);
    }

    [System.Diagnostics.CodeAnalysis.Experimental(ExperimentalApi.AgentRegistry, UrlFormat = ExperimentalApi.UrlFormat)]
    public static class ConsentPolicy
    {
        public const string AdminKeyRequired = "Admin key required";

        /// <summary>
        /// Whether StartSession may go ahead, and whether the person at the PC must be asked.
        /// - AGENT_ADMIN_KEY set and the caller's x-admin-key doesn't match: refused.
        /// - AGENT_ADMIN_KEY not set: allowed, but always asks (no unattended starts without an admin key).
        /// - Otherwise the installer's ceiling is the loosest a session may be: the prompt is skipped only when the
        ///   remote user asked for unattended AND the agent was installed with --consent=unattended.
        /// </summary>
        public static StartDecision Decide(bool unattended, ConsentMode ceiling, bool adminKeyConfigured, bool adminKeyValid)
        {
            if (!adminKeyConfigured)
                return StartDecision.Allow(requireConsent: true);
            if (!adminKeyValid)
                return StartDecision.Refuse(AdminKeyRequired);
            return StartDecision.Allow(requireConsent: !(unattended && ceiling == ConsentMode.ConsentUnattendedAllowed));
        }
    }
}
