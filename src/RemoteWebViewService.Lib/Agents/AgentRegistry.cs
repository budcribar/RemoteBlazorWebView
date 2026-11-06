using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using PeakSWC.RemoteWebView.Agent;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace PeakSWC.RemoteWebView.Agents
{
    /// <summary>One live AgentHub.Connect stream, as handed out by <see cref="AgentRegistry.Register"/>.</summary>
    public sealed class AgentConnection(string machineId, long id, ChannelReader<HubCommand> commands, CancellationToken replaced, bool replacedExisting, bool isNew)
    {
        public string MachineId { get; } = machineId;
        public long Id { get; } = id;
        public ChannelReader<HubCommand> Commands { get; } = commands;
        /// <summary>Cancelled when another stream registers for the same machine_id.</summary>
        public CancellationToken Replaced { get; } = replaced;
        public bool ReplacedExisting { get; } = replacedExisting;
        public bool IsNew { get; } = isNew;
    }

    /// <param name="Denied">The admin key was missing or wrong; the service turns this into PermissionDenied.</param>
    public sealed record StartSessionOutcome(StartSessionResponse Response, string MachineName, bool RequireConsent, bool Denied = false);

    public enum CancelLaunchOutcome { NotFound, Queued, AlreadyFinished }

    public sealed record SweepResult(IReadOnlyList<string> WentOffline, IReadOnlyList<string> Forgotten);

    public sealed class LaunchSubscription : IDisposable
    {
        private readonly Action onDispose;
        internal LaunchSubscription(ChannelReader<LaunchProgress> reader, Action onDispose)
        {
            Reader = reader;
            this.onDispose = onDispose;
        }
        public ChannelReader<LaunchProgress> Reader { get; }
        public void Dispose() => onDispose();
    }

    /// <summary>
    /// In-memory registry of Remote PC Diagnostics agents and the launches started on them.
    /// All state is guarded by one lock: agents report every 30 s and launches are rare, so contention is negligible.
    /// </summary>
    public sealed class AgentRegistry
    {
        public static readonly TimeSpan OfflineAfter = TimeSpan.FromSeconds(90);
        public static readonly TimeSpan ForgetAgentAfter = TimeSpan.FromDays(7);
        public static readonly TimeSpan ForgetLaunchAfter = TimeSpan.FromDays(1);
        // A start that hasn't reported anything for this long no longer blocks a new one.
        public static readonly TimeSpan InProgressWindow = TimeSpan.FromMinutes(3);

        public const string OfflineError = "That computer is offline";
        public const string BusyError = "A session is already running";
        public const string UnknownAgentError = "That computer isn't registered with the relay";
        public const string NoAgentError = "No computer was chosen";
        public const string InProgressError = "A start is already in progress for that computer";
        public const string DisconnectedDetail = "The computer disconnected";

        private readonly object gate = new();
        private readonly Dictionary<string, AgentEntry> agents = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, LaunchEntry> launches = new(StringComparer.OrdinalIgnoreCase);
        private readonly TimeProvider time;
        private TaskCompletionSource changed = NewSignal();
        private long nextConnectionId;

        public AgentRegistry(TimeProvider? time = null)
        {
            this.time = time ?? TimeProvider.System;
        }

        /// <summary>Completes on the next change. Grab it before reading the list so no change is missed.</summary>
        public Task Changed
        {
            get { lock (gate) return changed.Task; }
        }

        public static bool IsWatchTerminal(LaunchStage stage) =>
            stage is LaunchStage.StageReady or LaunchStage.StageDeclined or LaunchStage.StageFailed or LaunchStage.StageEnded;

        // After these nothing more is expected for the request. READY is still followed by ENDED when the app exits.
        private static bool IsFinal(LaunchStage stage) =>
            stage is LaunchStage.StageDeclined or LaunchStage.StageFailed or LaunchStage.StageEnded;

        public AgentConnection Register(AgentHello hello, string ipAddress, IServerStreamWriter<HubCommand>? writer)
        {
            ArgumentNullException.ThrowIfNull(hello);
            if (string.IsNullOrWhiteSpace(hello.MachineId))
                throw new ArgumentException("machine_id is required", nameof(hello));

            CancellationTokenSource? previous;
            AgentConnection connection;
            lock (gate)
            {
                bool isNew = !agents.TryGetValue(hello.MachineId, out var entry);
                if (entry == null)
                {
                    entry = new AgentEntry(hello.MachineId);
                    agents[hello.MachineId] = entry;
                }

                previous = entry.ConnectionCts;
                entry.ConnectionCts = new CancellationTokenSource();
                entry.ConnectionId = ++nextConnectionId;
                entry.Writer = writer;
                entry.IpAddress = ipAddress ?? string.Empty;
                entry.Online = true;
                entry.LastSeen = time.GetUtcNow();

                var info = entry.Info;
                info.MachineName = hello.MachineName;
                info.OsVersion = hello.OsVersion;
                info.SignedInUser = hello.SignedInUser;
                info.AgentVersion = hello.AgentVersion;
                info.CachedAppVersion = hello.CachedAppVersion;
                info.ConsentCeiling = hello.ConsentCeiling;
                info.Lifetime = hello.Lifetime;

                connection = new AgentConnection(entry.MachineId, entry.ConnectionId, entry.Commands.Reader, entry.ConnectionCts.Token, previous != null, isNew);
                NotifyChangedNoLock();
            }

            // Outside the lock: cancelling runs the old stream's registrations.
            previous?.Cancel();
            return connection;
        }

        /// <summary>Called when a Connect stream ends. Returns true when that stream was still the agent's current one.</summary>
        public bool Disconnect(AgentConnection connection) => Disconnect(connection, out _);

        /// <summary>
        /// As <see cref="Disconnect(AgentConnection)"/>, and fails the agent's starts that hadn't reached READY or a final stage
        /// ("The computer disconnected"). A READY launch is left alone: the app may still be running.
        /// </summary>
        public bool Disconnect(AgentConnection connection, out IReadOnlyList<LaunchProgress> failedLaunches)
        {
            var failed = new List<LaunchProgress>();
            failedLaunches = failed;
            lock (gate)
            {
                if (!TryGetCurrentNoLock(connection, out var entry))
                    return false;
                entry.ConnectionId = 0;
                entry.ConnectionCts = null;
                entry.Writer = null;
                entry.Online = false;

                foreach (var launch in launches.Values)
                {
                    if (!IsInFlight(launch, entry.MachineId))
                        continue;
                    var stored = PublishNoLock(launch, new LaunchProgress
                    {
                        RequestId = launch.RequestId,
                        Stage = LaunchStage.StageFailed,
                        SessionId = launch.SessionId,
                        Detail = DisconnectedDetail,
                    });
                    if (stored != null)
                        failed.Add(stored);
                }

                NotifyChangedNoLock();
                return true;
            }
        }

        public void OnHeartbeat(AgentConnection connection, Heartbeat heartbeat)
        {
            lock (gate)
            {
                if (!TryGetCurrentNoLock(connection, out var entry))
                    return;

                var info = entry.Info;
                bool changedAnything = !entry.Online
                    || info.SignedInUser != heartbeat.SignedInUser
                    || info.SessionRunning != heartbeat.SessionRunning
                    || info.SessionId != heartbeat.SessionId
                    || info.CachedAppVersion != heartbeat.CachedAppVersion;

                entry.Online = true;
                entry.LastSeen = time.GetUtcNow();
                info.SignedInUser = heartbeat.SignedInUser;
                info.SessionRunning = heartbeat.SessionRunning;
                info.SessionId = heartbeat.SessionId;
                info.CachedAppVersion = heartbeat.CachedAppVersion;

                // last_seen alone is refreshed by the periodic WatchAgents push; don't wake every watcher every 30 s per agent.
                if (changedAnything)
                    NotifyChangedNoLock();
            }
        }

        /// <summary>Stores progress from an agent and fans it out. Returns the stored copy, or null when it was ignored.</summary>
        public LaunchProgress? OnProgress(AgentConnection connection, LaunchProgress progress)
        {
            lock (gate)
            {
                if (!TryGetCurrentNoLock(connection, out var entry))
                    return null;

                entry.Online = true;
                entry.LastSeen = time.GetUtcNow();

                if (string.IsNullOrEmpty(progress.RequestId))
                    return null;

                if (!launches.TryGetValue(progress.RequestId, out var launch))
                {
                    // e.g. the relay restarted mid-launch: keep tracking it under the reporting agent.
                    launch = new LaunchEntry(progress.RequestId, entry.MachineId, progress.SessionId)
                    {
                        Latest = new LaunchProgress { RequestId = progress.RequestId, Stage = LaunchStage.StageUnknown, SessionId = progress.SessionId },
                    };
                    launches[launch.RequestId] = launch;
                }
                else if (!string.Equals(launch.MachineId, entry.MachineId, StringComparison.OrdinalIgnoreCase))
                {
                    return null; // an agent may only report on its own launches
                }

                var stored = PublishNoLock(launch, progress);
                if (stored == null)
                    return null;

                var info = entry.Info;
                if (stored.Stage == LaunchStage.StageReady)
                {
                    info.SessionRunning = true;
                    info.SessionId = stored.SessionId;
                }
                else if (stored.Stage == LaunchStage.StageEnded &&
                         (string.IsNullOrEmpty(info.SessionId) || string.Equals(info.SessionId, stored.SessionId, StringComparison.OrdinalIgnoreCase)))
                {
                    info.SessionRunning = false;
                    info.SessionId = string.Empty;
                }

                NotifyChangedNoLock();
                return stored;
            }
        }

        public StartSessionOutcome StartSession(string machineId, bool unattended, string technician, string serverUrl, bool adminKeyConfigured, bool adminKeyValid)
        {
            lock (gate)
            {
                AgentEntry? entry = null;
                if (!string.IsNullOrWhiteSpace(machineId))
                    agents.TryGetValue(machineId, out entry);

                // The admin key is checked before anything about the computer is revealed.
                var decision = ConsentPolicy.Decide(unattended, entry?.Info.ConsentCeiling ?? ConsentMode.ConsentAlwaysAsk, adminKeyConfigured, adminKeyValid);
                if (!decision.Allowed)
                    return new StartSessionOutcome(new StartSessionResponse { Accepted = false, Error = decision.Error ?? ConsentPolicy.AdminKeyRequired }, string.Empty, true, Denied: true);

                if (string.IsNullOrWhiteSpace(machineId))
                    return Refused(NoAgentError, string.Empty);
                if (entry == null)
                    return Refused(UnknownAgentError, string.Empty);
                if (!entry.Online || entry.ConnectionId == 0)
                    return Refused(OfflineError, entry.Info.MachineName);
                if (entry.Info.SessionRunning)
                    return Refused(BusyError, entry.Info.MachineName);
                var now = time.GetUtcNow();
                string id = entry.MachineId;
                if (launches.Values.Any(l => IsInFlight(l, id) && now - l.Updated < InProgressWindow))
                    return Refused(InProgressError, entry.Info.MachineName);

                string sessionId = Guid.NewGuid().ToString();
                string requestId = Guid.NewGuid().ToString();
                bool requireConsent = decision.RequireConsent;
                string baseUrl = (serverUrl ?? string.Empty).TrimEnd('/');

                var launch = new LaunchEntry(requestId, entry.MachineId, sessionId)
                {
                    Latest = new LaunchProgress
                    {
                        RequestId = requestId,
                        Stage = LaunchStage.StageQueued,
                        SessionId = sessionId,
                        Detail = "Sent to the computer",
                    },
                    Updated = time.GetUtcNow(),
                };
                launches[requestId] = launch;

                entry.Commands.Writer.TryWrite(new HubCommand
                {
                    Launch = new LaunchCommand
                    {
                        RequestId = requestId,
                        SessionId = sessionId,
                        ServerUrl = baseUrl,
                        Technician = technician ?? string.Empty,
                        RequireConsent = requireConsent,
                    }
                });

                return new StartSessionOutcome(new StartSessionResponse
                {
                    Accepted = true,
                    RequestId = requestId,
                    SessionId = sessionId,
                    AppUrl = baseUrl + "/app/" + sessionId,
                }, entry.Info.MachineName, requireConsent);
            }

            static StartSessionOutcome Refused(string error, string machineName) =>
                new(new StartSessionResponse { Accepted = false, Error = error }, machineName, true);
        }

        public CancelLaunchOutcome CancelLaunch(string requestId)
        {
            lock (gate)
            {
                if (string.IsNullOrEmpty(requestId) || !launches.TryGetValue(requestId, out var launch))
                    return CancelLaunchOutcome.NotFound;
                if (IsFinal(launch.Latest.Stage))
                    return CancelLaunchOutcome.AlreadyFinished;
                if (!agents.TryGetValue(launch.MachineId, out var entry))
                    return CancelLaunchOutcome.NotFound;

                entry.Commands.Writer.TryWrite(new HubCommand { Cancel = new CancelCommand { RequestId = requestId } });
                return CancelLaunchOutcome.Queued;
            }
        }

        /// <summary>Replays the latest stage, then live updates; the reader completes after a terminal stage. Null when unknown.</summary>
        public LaunchSubscription? SubscribeLaunch(string requestId)
        {
            lock (gate)
            {
                if (string.IsNullOrEmpty(requestId) || !launches.TryGetValue(requestId, out var launch))
                    return null;

                var channel = Channel.CreateUnbounded<LaunchProgress>();
                channel.Writer.TryWrite(launch.Latest);
                if (IsWatchTerminal(launch.Latest.Stage))
                {
                    channel.Writer.TryComplete();
                    return new LaunchSubscription(channel.Reader, () => { });
                }

                launch.Subscribers.Add(channel);
                return new LaunchSubscription(channel.Reader, () =>
                {
                    lock (gate) launch.Subscribers.Remove(channel);
                    channel.Writer.TryComplete();
                });
            }
        }

        public LaunchProgress? GetLatestProgress(string requestId)
        {
            lock (gate)
                return launches.TryGetValue(requestId, out var launch) ? launch.Latest : null;
        }

        public AgentInfo? GetAgent(string machineId)
        {
            lock (gate)
                return agents.TryGetValue(machineId, out var entry) ? SnapshotNoLock(entry) : null;
        }

        public AgentList GetAgentList()
        {
            var list = new AgentList();
            lock (gate)
            {
                list.Agents.AddRange(agents.Values
                    .OrderByDescending(e => e.Online)
                    .ThenBy(e => e.Info.MachineName, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(e => e.MachineId, StringComparer.OrdinalIgnoreCase)
                    .Select(SnapshotNoLock));
            }
            return list;
        }

        /// <summary>Marks silent agents offline and forgets old agents and launches. Run every few seconds.</summary>
        public SweepResult Sweep()
        {
            var now = time.GetUtcNow();
            var wentOffline = new List<string>();
            var forgotten = new List<string>();
            List<CancellationTokenSource> toCancel = [];

            lock (gate)
            {
                SweepAgentsNoLock(now, wentOffline, forgotten, toCancel);
                SweepLaunchesNoLock(now);

                if (wentOffline.Count > 0 || forgotten.Count > 0)
                    NotifyChangedNoLock();
            }

            foreach (var cts in toCancel)
                cts.Cancel();
            return new SweepResult(wentOffline, forgotten);
        }

        private void SweepAgentsNoLock(DateTimeOffset now, List<string> wentOffline, List<string> forgotten, List<CancellationTokenSource> toCancel)
        {
            foreach (var entry in agents.Values.ToList())
            {
                var silence = now - entry.LastSeen;
                if (entry.Online && silence >= OfflineAfter)
                {
                    entry.Online = false;
                    wentOffline.Add(Describe(entry));
                }
                else if (!entry.Online && silence >= ForgetAgentAfter)
                {
                    if (entry.ConnectionCts != null)
                        toCancel.Add(entry.ConnectionCts);
                    agents.Remove(entry.MachineId);
                    forgotten.Add(Describe(entry));
                }
            }
        }

        private void SweepLaunchesNoLock(DateTimeOffset now)
        {
            foreach (var launch in launches.Values.ToList())
            {
                if (now - launch.Updated >= ForgetLaunchAfter)
                {
                    foreach (var subscriber in launch.Subscribers)
                        subscriber.Writer.TryComplete();
                    launches.Remove(launch.RequestId);
                }
            }
        }

        private LaunchProgress? PublishNoLock(LaunchEntry launch, LaunchProgress progress)
        {
            if (IsFinal(launch.Latest.Stage))
                return null;

            var stored = progress.Clone();
            if (string.IsNullOrEmpty(stored.SessionId))
                stored.SessionId = launch.SessionId;
            launch.Latest = stored;
            launch.Updated = time.GetUtcNow();

            foreach (var subscriber in launch.Subscribers)
                subscriber.Writer.TryWrite(stored);

            if (IsWatchTerminal(stored.Stage))
            {
                foreach (var subscriber in launch.Subscribers)
                    subscriber.Writer.TryComplete();
                launch.Subscribers.Clear();
            }
            return stored;
        }

        // Started on this machine and not yet READY, DECLINED, FAILED or ENDED.
        private static bool IsInFlight(LaunchEntry launch, string machineId) =>
            string.Equals(launch.MachineId, machineId, StringComparison.OrdinalIgnoreCase) && !IsWatchTerminal(launch.Latest.Stage);

        private bool TryGetCurrentNoLock(AgentConnection connection, out AgentEntry entry)
        {
            if (agents.TryGetValue(connection.MachineId, out var found) && found.ConnectionId == connection.Id && connection.Id != 0)
            {
                entry = found;
                return true;
            }
            entry = null!;
            return false;
        }

        private static AgentInfo SnapshotNoLock(AgentEntry entry)
        {
            var info = entry.Info.Clone();
            info.Online = entry.Online;
            info.LastSeen = Timestamp.FromDateTimeOffset(entry.LastSeen);
            info.IpAddress = entry.IpAddress;
            return info;
        }

        private static string Describe(AgentEntry entry) =>
            string.IsNullOrEmpty(entry.Info.MachineName) ? entry.MachineId : $"{entry.Info.MachineName} ({entry.MachineId})";

        private void NotifyChangedNoLock()
        {
            var previous = changed;
            changed = NewSignal();
            previous.TrySetResult();
        }

        private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

        private sealed class AgentEntry(string machineId)
        {
            public string MachineId { get; } = machineId;
            public AgentInfo Info { get; } = new() { MachineId = machineId };
            public DateTimeOffset LastSeen { get; set; }
            public bool Online { get; set; }
            public string IpAddress { get; set; } = string.Empty;
            public long ConnectionId { get; set; }
            public CancellationTokenSource? ConnectionCts { get; set; }
            public IServerStreamWriter<HubCommand>? Writer { get; set; }
            // Per agent, not per stream: a reconnecting agent picks up what was queued for it.
            public Channel<HubCommand> Commands { get; } = Channel.CreateUnbounded<HubCommand>();
        }

        private sealed class LaunchEntry(string requestId, string machineId, string sessionId)
        {
            public string RequestId { get; } = requestId;
            public string MachineId { get; } = machineId;
            public string SessionId { get; } = sessionId;
            public LaunchProgress Latest { get; set; } = new();
            public DateTimeOffset Updated { get; set; }
            public List<Channel<LaunchProgress>> Subscribers { get; } = [];
        }
    }
}
