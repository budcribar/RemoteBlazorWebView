using PeakSWC.RemoteWebView.Agent;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Channels;

namespace PeakSWC.RemoteWebView.Agents
{
    /// <summary>A WatchAlerts subscriber. Dispose to stop receiving uploads.</summary>
    public sealed class AlertsSubscription : IDisposable
    {
        private readonly Action onDispose;

        internal AlertsSubscription(ChannelReader<AlertUpload> reader, Action onDispose)
        {
            Reader = reader;
            this.onDispose = onDispose;
        }

        public ChannelReader<AlertUpload> Reader { get; }
        public void Dispose() => onDispose();
    }

    /// <summary>
    /// Alerts, the same way as scheduled metrics: agents upload alerts from their outbox, the relay hands them to every Admin
    /// subscriber that may see that computer, and Admin acknowledges what it saved. The relay keeps nothing.
    /// </summary>
    public sealed partial class AgentRegistry
    {
        /// <summary>Most alerts the relay passes on from one agent message; the agent sends at most 50.</summary>
        public const int MaxAlertsPerUpload = 50;
        public const int MaxFactsPerAlert = 8;
        public const int MaxSourceEventsPerAlert = 4;
        public const int MaxRelatedChecksPerAlert = 4;
        public const int MaxDisabledAlertKinds = 32;

        private readonly List<(Channel<AlertUpload> Channel, string? GroupScope)> alertWatchers = [];

        /// <summary>Starts a WatchAlerts stream for the computers in <paramref name="groupScope"/> (null = every computer).</summary>
        public AlertsSubscription SubscribeAlerts(string? groupScope)
        {
            var channel = Channel.CreateBounded<AlertUpload>(new BoundedChannelOptions(SubscriberBuffer)
            {
                FullMode = BoundedChannelFullMode.DropWrite,
                SingleReader = true,
            });
            var watcher = (channel, groupScope);
            lock (gate)
                alertWatchers.Add(watcher);
            return new AlertsSubscription(channel.Reader, () =>
            {
                lock (gate)
                    alertWatchers.Remove(watcher);
                channel.Writer.TryComplete();
            });
        }

        /// <summary>
        /// Passes an agent's alerts on to the subscribers that may see it. Returns how many subscribers got them (0 means nobody will
        /// acknowledge, and the agent keeps them). Alerts are trimmed to the limits above; the text in them is the PC's, not instructions.
        /// </summary>
        public int OnAlerts(AgentConnection connection, AlertBatch batch)
        {
            ArgumentNullException.ThrowIfNull(batch);
            lock (gate)
            {
                if (!TryGetCurrentNoLock(connection, out var entry))
                    return 0;
                var upload = new AlertUpload
                {
                    MachineId = entry.MachineId,
                    MachineName = entry.Info.MachineName,
                    Group = entry.Group,
                };
                upload.Alerts.AddRange(batch.Alerts.Where(a => !string.IsNullOrWhiteSpace(a.AlertId) && !string.IsNullOrWhiteSpace(a.Kind) && a.Seq > 0)
                    .Take(MaxAlertsPerUpload).Select(Trim));
                if (upload.Alerts.Count == 0)
                    return 0;

                var delivered = 0;
                foreach (var (channel, scope) in alertWatchers)
                {
                    if (scope != null && !string.Equals(scope, entry.Group, StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (channel.Writer.TryWrite(upload.Clone()))
                        delivered++;
                }
                return delivered;
            }
        }

        /// <summary>Tells the agent Admin saved its alerts through <paramref name="throughSeq"/>. False when it is offline or not visible.</summary>
        public bool AckAlerts(string machineId, long throughSeq, string? groupScope, out string error)
        {
            if (throughSeq <= 0)
            {
                error = "through_seq must be positive";
                return false;
            }
            return SendIfOnline(machineId, groupScope, new HubCommand { AlertsAck = new AlertsAckCommand { ThroughSeq = throughSeq } }, out error);
        }

        /// <summary>Sends the alert setting. Offline computers get it when they reconnect and report an older revision to Admin.</summary>
        public bool SetAlertSettings(string machineId, AlertSettingsCommand settings, string? groupScope, out string error)
        {
            ArgumentNullException.ThrowIfNull(settings);
            if (settings.Revision <= 0)
            {
                error = "revision must be positive";
                return false;
            }
            var copy = settings.Clone();
            var kinds = copy.DisabledKinds.Where(k => !string.IsNullOrWhiteSpace(k)).Select(k => Cut(k.Trim(), 64))
                .Distinct(StringComparer.OrdinalIgnoreCase).Take(MaxDisabledAlertKinds).ToList();
            copy.DisabledKinds.Clear();
            copy.DisabledKinds.AddRange(kinds);
            copy.LowDiskFreeGb = Finite(copy.LowDiskFreeGb);
            copy.LowDiskFreePercent = Finite(copy.LowDiskFreePercent);
            copy.MemoryCommitPercent = Finite(copy.MemoryCommitPercent);
            return SendIfOnline(machineId, groupScope, new HubCommand { AlertSettings = copy }, out error);
        }

        /// <summary>Tells the agent an alert was handled or cleared. Only those two states can be sent.</summary>
        public bool SetAlertState(string machineId, string alertId, AlertState state, string? groupScope, out string error)
        {
            if (string.IsNullOrWhiteSpace(alertId))
            {
                error = "alert_id is required";
                return false;
            }
            if (state is not (AlertState.AlertHandled or AlertState.AlertCleared))
            {
                error = "state must be handled or cleared";
                return false;
            }
            return SendIfOnline(machineId, groupScope, new HubCommand { AlertState = new AlertStateCommand { AlertId = Cut(alertId.Trim(), 64), State = state } }, out error);
        }

        private static double Finite(double value) => double.IsFinite(value) && value > 0 ? value : 0;

        private static AgentAlert Trim(AgentAlert alert)
        {
            var copy = new AgentAlert
            {
                AlertId = Cut(alert.AlertId, 64),
                Seq = alert.Seq,
                Kind = Cut(alert.Kind, 64),
                Severity = alert.Severity,
                InstanceId = Cut(alert.InstanceId, 128),
                InstanceLabel = Cut(alert.InstanceLabel, 128),
                FirstSeen = alert.FirstSeen,
                LastSeen = alert.LastSeen,
                Count = Math.Max(1, alert.Count),
                State = alert.State,
                Summary = Cut(alert.Summary, MaxText),
                AppVersion = Cut(alert.AppVersion, 64),
            };
            copy.Facts.AddRange(alert.Facts.Where(f => !string.IsNullOrWhiteSpace(f.Name)).Take(MaxFactsPerAlert)
                .Select(f => new AlertFact { Name = Cut(f.Name, 64), Value = Cut(f.Value, 128) }));
            copy.SourceEvents.AddRange(alert.SourceEvents.TakeLast(MaxSourceEventsPerAlert)
                .Select(e => new AlertSourceEvent { Channel = Cut(e.Channel, 64), Provider = Cut(e.Provider, 128), EventId = e.EventId, RecordId = e.RecordId }));
            copy.RelatedChecks.AddRange(alert.RelatedChecks.Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => Cut(c.Trim(), 64))
                .Distinct(StringComparer.OrdinalIgnoreCase).Take(MaxRelatedChecksPerAlert));
            return copy;
        }
    }
}
