using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace PeakSWC.RemoteWebView.Services
{
    public class ShutdownService(ILogger<RemoteWebViewService> logger, ConcurrentDictionary<string, Channel<string>> serviceStateChannel, ConcurrentDictionary<string, TaskCompletionSource<ServiceState>> serviceDictionary)
    {
        /// <summary>
        /// Tears the session down and logs why, once, at Warning (the production relay drops Information), with who the app was and
        /// how long the session ran. A second call for a session that is already gone logs at Debug only.
        /// </summary>
        /// <param name="reason">Why the session ends.</param>
        /// <param name="exception">The error behind it, when there is one.</param>
        /// <param name="detail">Extra context for the log line, e.g. which browser connection left.</param>
        public async Task Shutdown(string id, SessionEndReason reason, Exception? exception = null, string? detail = null)
        {
            if (string.IsNullOrWhiteSpace(id))
                return;

            if (serviceDictionary.TryRemove(id, out var client))
            {
                try
                {
                    ServiceState? known = client.Task.IsCompletedSuccessfully ? client.Task.Result : null;
                    LogShutdown(id, reason, exception, detail, known);

                    var serviceState = known ?? await client.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                    try
                    {
                        await (serviceState.IPC?.WriteToClientAsync(new WebMessageResponse { Response = "shutdown:" }) ?? Task.CompletedTask).ConfigureAwait(false);
                    }
                    catch (Exception writeEx)
                    {
                        logger.LogError(writeEx, "Failed writing shutdown message to client {Id}.", id);
                    }
                    serviceState.InUse = false;
                    await serviceState.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed shutdown for client {Id}.", id);
                }

                try
                {
                    foreach (var channel in serviceStateChannel.Values)
                    {
                        if (!channel.Writer.TryWrite($"Shutdown:{id}"))
                        {
                            logger.LogError("Failed to write shutdown notification to channel for client {Id}.", id);
                        }
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed shutdown for client {Id} during channel notification.", id);
                }
            }
            else
            {
                logger.LogDebug("Shutdown for session {Id} ({Reason}) ignored: it has already ended.", id, reason);
            }
        }

        private void LogShutdown(string id, SessionEndReason reason, Exception? exception, string? detail, ServiceState? state)
        {
            const string Template = "Session {Id} shut down: {Reason}{Detail}. App: {ProcessName} pid {Pid} on {HostName}, group {Group}, "
                + "ran {Duration}, controller {Primary}, owner {Owner}, max ping {MaxPing} ms";
            var suffix = string.IsNullOrWhiteSpace(detail) ? string.Empty : " (" + detail + ")";
            var duration = state is null ? "unknown" : (DateTime.UtcNow - state.StartTime).ToString(@"d\.hh\:mm\:ss", System.Globalization.CultureInfo.InvariantCulture);
            object[] args =
            [
                id, reason, suffix, state?.ProcessName ?? "unknown", state?.Pid ?? 0, state?.HostName ?? "unknown", state?.Group ?? "unknown",
                duration, Or(state?.PrimaryClientId), Or(state?.OwnerClientId), (int)(state?.MaxClientPing.TotalMilliseconds ?? 0),
            ];
            if (exception is null)
                logger.LogWarning(Template, args);
            else
                logger.LogWarning(exception, Template, args);
        }

        private static string Or(string? value) => string.IsNullOrEmpty(value) ? "none" : value;
    }
}
