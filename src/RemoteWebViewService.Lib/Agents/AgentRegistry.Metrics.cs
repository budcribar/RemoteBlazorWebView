using PeakSWC.RemoteWebView.Agent;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Channels;

namespace PeakSWC.RemoteWebView.Agents
{
    /// <summary>A WatchMetrics subscriber. Dispose to stop receiving uploads.</summary>
    public sealed class MetricsSubscription : IDisposable
    {
        private readonly Action onDispose;

        internal MetricsSubscription(ChannelReader<MetricsUpload> reader, Action onDispose)
        {
            Reader = reader;
            this.onDispose = onDispose;
        }

        public ChannelReader<MetricsUpload> Reader { get; }
        public void Dispose() => onDispose();
    }

    /// <summary>
    /// Scheduled metrics: agents upload runs from their outbox, the relay hands them to every Admin subscriber that may see that
    /// computer, and Admin acknowledges what it saved. The relay keeps nothing: a run nobody acknowledged stays in the agent's
    /// outbox and comes again with its next upload, so a relay restart or an Admin outage loses nothing.
    /// </summary>
    public sealed partial class AgentRegistry
    {
        /// <summary>Most runs the relay passes on from one agent message; the agent sends at most 50.</summary>
        public const int MaxRunsPerUpload = 50;
        public const int MaxMetricsPerRun = 16;
        private const int MaxText = 300;
        /// <summary>Uploads a slow subscriber may fall behind before new ones are dropped for it (unacknowledged, so the agent resends).</summary>
        private const int SubscriberBuffer = 256;

        private readonly List<(Channel<MetricsUpload> Channel, string? GroupScope)> metricsWatchers = [];

        /// <summary>Starts a WatchMetrics stream for the computers in <paramref name="groupScope"/> (null = every computer).</summary>
        public MetricsSubscription SubscribeMetrics(string? groupScope)
        {
            var channel = Channel.CreateBounded<MetricsUpload>(new BoundedChannelOptions(SubscriberBuffer)
            {
                FullMode = BoundedChannelFullMode.DropWrite,
                SingleReader = true,
            });
            var watcher = (channel, groupScope);
            lock (gate)
                metricsWatchers.Add(watcher);
            return new MetricsSubscription(channel.Reader, () =>
            {
                lock (gate)
                    metricsWatchers.Remove(watcher);
                channel.Writer.TryComplete();
            });
        }

        /// <summary>
        /// Passes an agent's runs on to the subscribers that may see it. Returns how many subscribers got them (0 means nobody will
        /// acknowledge, and the agent keeps them). Runs are trimmed to the limits above; the text in them is the PC's, not instructions.
        /// </summary>
        public int OnMetrics(AgentConnection connection, MetricsBatch batch)
        {
            ArgumentNullException.ThrowIfNull(batch);
            lock (gate)
            {
                if (!TryGetCurrentNoLock(connection, out var entry))
                    return 0;
                var upload = new MetricsUpload
                {
                    MachineId = entry.MachineId,
                    MachineName = entry.Info.MachineName,
                    Group = entry.Group,
                };
                upload.Runs.AddRange(batch.Runs.Where(r => !string.IsNullOrWhiteSpace(r.RunId) && r.Seq > 0).Take(MaxRunsPerUpload).Select(Trim));
                if (upload.Runs.Count == 0)
                    return 0;

                var delivered = 0;
                foreach (var (channel, scope) in metricsWatchers)
                {
                    if (scope != null && !string.Equals(scope, entry.Group, StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (channel.Writer.TryWrite(upload.Clone()))
                        delivered++;
                }
                return delivered;
            }
        }

        /// <summary>Tells the agent Admin saved its runs through <paramref name="throughSeq"/>. False when it is offline or not visible.</summary>
        public bool AckMetrics(string machineId, long throughSeq, string? groupScope, out string error)
        {
            if (throughSeq <= 0)
            {
                error = "through_seq must be positive";
                return false;
            }
            return SendIfOnline(machineId, groupScope, new HubCommand { MetricsAck = new MetricsAckCommand { ThroughSeq = throughSeq } }, out error);
        }

        /// <summary>Sends the scheduled-metrics setting. Offline computers get it when they reconnect and report an older revision to Admin.</summary>
        public bool SetMetricsSchedule(string machineId, MetricsScheduleCommand schedule, string? groupScope, out string error)
        {
            ArgumentNullException.ThrowIfNull(schedule);
            if (schedule.Revision <= 0)
            {
                error = "revision must be positive";
                return false;
            }
            var copy = schedule.Clone();
            var ids = copy.CheckIds.Where(id => !string.IsNullOrWhiteSpace(id)).Select(id => Cut(id.Trim(), 64)).Distinct(StringComparer.OrdinalIgnoreCase).Take(16).ToList();
            copy.CheckIds.Clear();
            copy.CheckIds.AddRange(ids);
            return SendIfOnline(machineId, groupScope, new HubCommand { MetricsSchedule = copy }, out error);
        }

        public bool CollectMetrics(string machineId, string? groupScope, out string error) =>
            SendIfOnline(machineId, groupScope, new HubCommand { CollectMetrics = new CollectMetricsCommand { RequestId = Guid.NewGuid().ToString() } }, out error);

        /// <summary>Asks the agent to stop an update that is still waiting for the PC to be free. Never queued for an offline computer.</summary>
        public bool CancelUpdateRpcd(string machineId, string? requestId, string? groupScope, out string error) =>
            SendIfOnline(machineId, groupScope, new HubCommand { CancelUpdateRpcd = new CancelUpdateRpcdCommand { RequestId = requestId?.Trim() ?? string.Empty } }, out error);

        private bool SendIfOnline(string machineId, string? groupScope, HubCommand command, out string error)
        {
            lock (gate)
            {
                if (!TryVisibleAgentNoLock(machineId, groupScope, out var entry, out error))
                    return false;
                // Never queued for an offline computer: the per-agent queue would grow while it is away, and Admin sends these again.
                if (!entry.Online || entry.ConnectionId == 0)
                {
                    error = OfflineError;
                    return false;
                }
                return entry.Commands.Writer.TryWrite(command);
            }
        }

        private static MetricsRun Trim(MetricsRun run)
        {
            var copy = new MetricsRun
            {
                RunId = Cut(run.RunId, 64),
                Seq = run.Seq,
                CheckId = Cut(run.CheckId, 64),
                InstanceId = Cut(run.InstanceId, 128),
                InstanceLabel = Cut(run.InstanceLabel, 128),
                Finished = run.Finished,
                Outcome = Cut(run.Outcome, 32),
                Summary = Cut(run.Summary, MaxText),
                AppVersion = Cut(run.AppVersion, 64),
            };
            copy.Metrics.AddRange(run.Metrics.Where(m => !string.IsNullOrWhiteSpace(m.Id) && double.IsFinite(m.Value)).Take(MaxMetricsPerRun)
                .Select(m => new MetricValue { Id = Cut(m.Id, 64), Label = Cut(m.Label, 64), Value = m.Value, Unit = Cut(m.Unit, 16), Better = m.Better }));
            return copy;
        }

        private static string Cut(string? text, int max) =>
            string.IsNullOrEmpty(text) ? string.Empty : text.Length <= max ? text : text[..max];
    }
}
