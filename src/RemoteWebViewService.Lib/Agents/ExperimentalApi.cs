namespace PeakSWC.RemoteWebView.Agents
{
    /// <summary>
    /// Diagnostic id for the agent registry (AgentHub / AgentAdmin). The feature is experimental and may change or be
    /// removed in a later release without a major version bump. Suppress RWV001 to use it.
    /// </summary>
    internal static class ExperimentalApi
    {
        public const string AgentRegistry = "RWV001";
        public const string UrlFormat = "https://github.com/budcribar/RemoteBlazorWebView/blob/net10/doc/Agents.md#experimental";
    }
}
