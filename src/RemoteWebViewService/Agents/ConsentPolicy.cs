using PeakSWC.RemoteWebView.Agent;

namespace PeakSWC.RemoteWebView.Agents
{
    public static class ConsentPolicy
    {
        // The installer's ceiling is the loosest a session may be. A technician can skip the prompt
        // only when they asked for unattended AND the agent was installed with --consent=unattended.
        public static bool RequireConsent(bool unattendedRequested, ConsentMode ceiling) =>
            !(unattendedRequested && ceiling == ConsentMode.ConsentUnattendedAllowed);
    }
}
