using Microsoft.Extensions.Logging;
using System;

namespace PeakSWC.RemoteWebView.Agents
{
    /// <summary>
    /// Agent registrations, launches and declines go to the normal logger (Information, enabled for this namespace in
    /// Startup) and straight into the in-memory event log, which the admin "Events" page reads on Linux.
    /// The in-memory provider only keeps warnings, so recording directly does not duplicate entries.
    /// </summary>
    internal static class AgentEvents
    {
        public static void Info(ILogger logger, string category, string message)
        {
            logger.LogInformation("{AgentEvent}", message);
            InMemoryEventLog.Record(DateTime.UtcNow, [$"Category: {category} [Information]", message]);
        }

        // Warnings are picked up by the in-memory provider itself.
        public static void Warn(ILogger logger, string message) => logger.LogWarning("{AgentEvent}", message);
    }
}
