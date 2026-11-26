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
        /// <summary>How long an ended session is remembered, so late calls for it (an app still sending after "Connection lost") log at Debug.</summary>
        public static readonly TimeSpan RememberEnded = TimeSpan.FromMinutes(15);

        private readonly ConcurrentDictionary<string, (SessionEndReason Reason, DateTime EndedUtc)> _ended = new(StringComparer.Ordinal);

        /// <summary>True when the session ended within <see cref="RememberEnded"/>, with why. Used only to choose a log level.</summary>
        public bool HasEnded(string id, out SessionEndReason reason)
        {
            reason = default;
            if (string.IsNullOrEmpty(id) || !_ended.TryGetValue(id, out var ended))
                return false;
            if (DateTime.UtcNow - ended.EndedUtc > RememberEnded)
            {
                _ended.TryRemove(KeyValuePair.Create(id, ended));
                return false;
            }
            reason = ended.Reason;
            return true;
        }

        /// <summary>A new session started under this id (an app reuses its id after a restart): it isn't the ended one any more.</summary>
        public void Forget(string id)
        {
            if (!string.IsNullOrEmpty(id))
                _ended.TryRemove(id, out _);
        }

        private void RememberEnd(string id, SessionEndReason reason)
        {
            var now = DateTime.UtcNow;
            foreach (var (key, value) in _ended)
            {
                if (now - value.EndedUtc > RememberEnded)
                    _ended.TryRemove(KeyValuePair.Create(key, value));
            }
            _ended[id] = (reason, now);
        }

        private void NotifyChannels(string id)
        {
            try
            {
                foreach (var channel in serviceStateChannel.Values)
                {
                    if (!channel.Writer.TryWrite($"Shutdown:{id}"))
                        logger.LogError("Failed to write shutdown notification to channel for client {Id}.", id);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed shutdown for client {Id} during channel notification.", id);
            }
        }

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
                    RememberEnd(id, reason);
                    LogShutdown(id, reason, exception, detail, known);

                    var serviceState = known ?? await client.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                    try
                    {
                        await (serviceState.IPC?.WriteToClientAsync(new WebMessageResponse { Response = "shutdown:" }) ?? Task.CompletedTask).ConfigureAwait(false);
                    }
                    catch (Exception writeEx) when (writeEx is InvalidOperationException or System.IO.IOException or OperationCanceledException)
                    {
                        // The app's stream is already complete or its connection is gone: there is nobody left to tell.
                        logger.LogDebug(writeEx, "Shutdown message to client {Id} not delivered: its stream has closed.", id);
                    }
                    catch (Exception writeEx)
                    {
                        logger.LogError(writeEx, "Failed writing shutdown message to client {Id}.", id);
                    }
                    serviceState.InUse = false;
                    await serviceState.EndAsync(reason).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed shutdown for client {Id}.", id);
                }

                NotifyChannels(id);
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
            const string Unknown = "unknown";
            var duration = state is null ? Unknown : (DateTime.UtcNow - state.StartTime).ToString(@"d\.hh\:mm\:ss", System.Globalization.CultureInfo.InvariantCulture);
            object[] args =
            [
                id, reason, suffix, state?.ProcessName ?? Unknown, state?.Pid ?? 0, state?.HostName ?? Unknown, state?.Group ?? Unknown,
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
