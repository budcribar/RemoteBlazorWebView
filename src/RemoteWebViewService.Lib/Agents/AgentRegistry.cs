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
    [System.Diagnostics.CodeAnalysis.Experimental(ExperimentalApi.AgentRegistry, UrlFormat = ExperimentalApi.UrlFormat)]
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
    [System.Diagnostics.CodeAnalysis.Experimental(ExperimentalApi.AgentRegistry, UrlFormat = ExperimentalApi.UrlFormat)]
    public sealed record StartSessionOutcome(StartSessionResponse Response, string MachineName, bool RequireConsent, bool Denied = false);

    [System.Diagnostics.CodeAnalysis.Experimental(ExperimentalApi.AgentRegistry, UrlFormat = ExperimentalApi.UrlFormat)]
    public enum CancelLaunchOutcome { NotFound, Queued, AlreadyFinished }

    /// <param name="RequestId">The launch that was told to stop, when <paramref name="Outcome"/> is requested.</param>
    /// <param name="SessionId">That launch's session ID, which is also the app's ID on the relay, when <paramref name="Outcome"/> is requested.</param>
    [System.Diagnostics.CodeAnalysis.Experimental(ExperimentalApi.AgentRegistry, UrlFormat = ExperimentalApi.UrlFormat)]
    public sealed record EndSessionResult(EndSessionOutcome Outcome, string RequestId = "", string MachineName = "", string SessionId = "");

    [System.Diagnostics.CodeAnalysis.Experimental(ExperimentalApi.AgentRegistry, UrlFormat = ExperimentalApi.UrlFormat)]
    public sealed record SweepResult(IReadOnlyList<string> WentOffline, IReadOnlyList<string> Forgotten);

    [System.Diagnostics.CodeAnalysis.Experimental(ExperimentalApi.AgentRegistry, UrlFormat = ExperimentalApi.UrlFormat)]
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

    /// <summary>The outcome of <see cref="AgentRegistry.UpdateRpcdAsync"/>.</summary>
    /// <param name="Progress">The agent's first snapshot, when the command was forwarded and answered.</param>
    /// <param name="Denied">The admin key was missing or wrong; the service turns this into PermissionDenied.</param>
    /// <param name="Offline">The computer is registered but its agent stream is down.</param>
    /// <param name="NotFound">No such computer for this caller.</param>
    /// <param name="TooOld">The agent never answered. Agents built before update_rpcd drop the command.</param>
    [System.Diagnostics.CodeAnalysis.Experimental(ExperimentalApi.AgentRegistry, UrlFormat = ExperimentalApi.UrlFormat)]
    public sealed record UpdateRpcdOutcome(
        UpdateRpcdProgress? Progress,
        string MachineName = "",
        bool Denied = false,
        bool Offline = false,
        bool NotFound = false,
        bool TooOld = false,
        string? Error = null);

    /// <summary>What <see cref="AgentRegistry.OpenUpdateWatch"/> decided.</summary>
    [System.Diagnostics.CodeAnalysis.Experimental(ExperimentalApi.AgentRegistry, UrlFormat = ExperimentalApi.UrlFormat)]
    public sealed record UpdateWatch(
        UpdateSubscription? Subscription,
        bool Denied = false,
        bool Offline = false,
        bool NotFound = false,
        string? Error = null);

    /// <summary>Replays the latest RPCD update snapshot, then live ones. Ends after a terminal stage.</summary>
    [System.Diagnostics.CodeAnalysis.Experimental(ExperimentalApi.AgentRegistry, UrlFormat = ExperimentalApi.UrlFormat)]
    public sealed class UpdateSubscription : IDisposable
    {
        private readonly Action onDispose;
        internal UpdateSubscription(ChannelReader<UpdateRpcdProgress> reader, bool replayed, Action onDispose)
        {
            Reader = reader;
            Replayed = replayed;
            this.onDispose = onDispose;
        }

        public ChannelReader<UpdateRpcdProgress> Reader { get; }
        /// <summary>True when <see cref="Reader"/> already holds a snapshot. An empty watch is waiting for the agent to answer.</summary>
        public bool Replayed { get; }
        public void Dispose() => onDispose();
    }

    /// <summary>
    /// In-memory registry of agents and the launches started on them.
    /// All state is guarded by one lock: agents report every 30 s and launches are rare, so contention is negligible.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.Experimental(ExperimentalApi.AgentRegistry, UrlFormat = ExperimentalApi.UrlFormat)]
    public sealed partial class AgentRegistry
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
        /// <summary>The same words the fleet agent uses when it has no update handler. An older agent never answers at all.</summary>
        public const string TooOldForUpdateError = "This agent cannot install RPCD updates.";
        public const string UpdateInProgressError = "An RPCD update is already in progress.";

        /// <summary>
        /// How long <see cref="UpdateRpcdAsync"/> waits for the first snapshot. A current agent answers before it downloads
        /// (its lock wait is a few seconds). An older agent drops the command and never answers.
        /// </summary>
        public TimeSpan UpdateAckTimeout { get; set; } = TimeSpan.FromSeconds(12);

        private readonly object gate = new();
        private readonly Dictionary<string, AgentEntry> agents = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, LaunchEntry> launches = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, UpdateEntry> updates = new(StringComparer.OrdinalIgnoreCase);
        private readonly TimeProvider time;
        private TaskCompletionSource changed = NewSignal();
        private long nextConnectionId;
        private ReleaseAvailableCommand? latestRelease;

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
            stage is LaunchStage.StageReady or LaunchStage.StageDeclined or LaunchStage.StageConsentTimedOut or LaunchStage.StageConditionsNotMet
                or LaunchStage.StageFailed or LaunchStage.StageEnded;

        // After these nothing more is expected for the request. READY is still followed by ENDED when the app exits.
        private static bool IsFinal(LaunchStage stage) =>
            stage is LaunchStage.StageDeclined or LaunchStage.StageConsentTimedOut or LaunchStage.StageConditionsNotMet or LaunchStage.StageFailed
                or LaunchStage.StageEnded;

        /// <summary>Terminal stages from the agent, plus <see cref="UpdateRpcdProgress.Terminal"/> when the agent set it.</summary>
        public static bool IsUpdateTerminal(UpdateRpcdProgress? progress)
        {
            if (progress == null || string.IsNullOrEmpty(progress.Stage))
                return false;
            return progress.Terminal
                || progress.Stage is "refused" or "already_up_to_date" or "succeeded" or "failed";
        }

        /// <summary>
        /// Caches the latest release versions and pushes a <see cref="ReleaseAvailableCommand"/> to every currently online agent.
        /// Newly connecting agents also receive the cached announcement right after <c>AgentHello</c>.
        /// </summary>
        public int PublishRelease(ReleaseAvailableCommand release)
        {
            ArgumentNullException.ThrowIfNull(release);
            var stable = release.StableVersion?.Trim() ?? string.Empty;
            var prerelease = release.PrereleaseVersion?.Trim() ?? string.Empty;
            if (stable.Length == 0 && prerelease.Length == 0)
                return 0;

            lock (gate)
            {
                latestRelease = new ReleaseAvailableCommand
                {
                    StableVersion = stable.Length > 0 ? stable : latestRelease?.StableVersion ?? string.Empty,
                    PrereleaseVersion = prerelease.Length > 0 ? prerelease : latestRelease?.PrereleaseVersion ?? string.Empty,
                };

                int notified = 0;
                foreach (var entry in agents.Values)
                {
                    if (!entry.Online)
                        continue;
                    if (entry.Commands.Writer.TryWrite(new HubCommand { ReleaseAvailable = latestRelease.Clone() }))
                        notified++;
                }
                return notified;
            }
        }

        public AgentConnection Register(AgentHello hello, string ipAddress, IServerStreamWriter<HubCommand>? writer, string group = "", string? issuedAgentToken = null)
        {
            ArgumentNullException.ThrowIfNull(hello);
            if (string.IsNullOrWhiteSpace(hello.MachineId))
                throw new ArgumentException("machine_id is required", nameof(hello));
            group ??= string.Empty;

            CancellationTokenSource? previous;
            AgentConnection connection;
            lock (gate)
            {
                bool isNew = !agents.TryGetValue(hello.MachineId, out var entry);
                // An offline computer keeps its shop, unless the authority just issued it a new token for the new shop
                // (the old shop reset it). A computer that is online in another shop is always refused.
                if (entry != null
                    && !string.IsNullOrEmpty(entry.Group)
                    && !string.Equals(entry.Group, group, StringComparison.OrdinalIgnoreCase)
                    && (entry.Online || string.IsNullOrWhiteSpace(issuedAgentToken)))
                    throw new InvalidOperationException("That computer is already enrolled to another shop.");

                // A newly issued token means the authority has already decided this connect (first enrollment, a replacement
                // token, or a reset), so the token remembered here is out of date and must not refuse it.
                if (entry?.AgentTokenHash != null && string.IsNullOrWhiteSpace(issuedAgentToken))
                {
                    var presentedHash = HashAgentToken(hello.AgentToken);
                    if (presentedHash == null || !System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(entry.AgentTokenHash, presentedHash))
                        throw new InvalidOperationException("The agent token for this computer is invalid.");
                }

                if (entry == null)
                {
                    entry = new AgentEntry(hello.MachineId);
                    agents[hello.MachineId] = entry;
                }

                if (!string.IsNullOrWhiteSpace(issuedAgentToken))
                {
                    entry.AgentTokenHash = HashAgentToken(issuedAgentToken);
                    entry.Commands.Writer.TryWrite(new HubCommand
                    {
                        AssignAgentToken = new AssignAgentTokenCommand { AgentToken = issuedAgentToken.Trim() },
                    });
                }
                else if (entry.AgentTokenHash == null && !string.IsNullOrWhiteSpace(hello.AgentToken))
                {
                    entry.AgentTokenHash = HashAgentToken(hello.AgentToken);
                }

                entry.Group = group;
                entry.Info.Group = entry.Group;
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
                info.BootTime = hello.BootTime?.Clone();
                info.MetricsScheduleRevision = hello.MetricsScheduleRevision;
                info.AlertSettingsRevision = hello.AlertSettingsRevision;
                info.UtcOffsetMinutes = hello.UtcOffsetMinutes;
                info.PowerSource = hello.PowerSource;

                if (latestRelease != null)
                    entry.Commands.Writer.TryWrite(new HubCommand { ReleaseAvailable = latestRelease.Clone() });

                // Setup replaces the service. The new process reads the snapshot from disk and answers a status poll,
                // including the version that landed. The command stays queued if this stream ends before it is sent.
                if (!string.IsNullOrEmpty(entry.LatestUpdateRequestId)
                    && updates.TryGetValue(entry.LatestUpdateRequestId, out var openUpdate)
                    && !IsUpdateTerminal(openUpdate.Latest))
                    QueueUpdateStatusNoLock(entry, openUpdate.RequestId);

                connection = new AgentConnection(entry.MachineId, entry.ConnectionId, entry.Commands.Reader, entry.ConnectionCts.Token, previous != null, isNew);
                NotifyChangedNoLock();
            }

            // Outside the lock: cancelling runs the old stream's registrations.
            previous?.Cancel();
            return connection;
        }

        private static byte[]? HashAgentToken(string? token) =>
            string.IsNullOrWhiteSpace(token)
                ? null
                : System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token.Trim()));

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
                    || info.CachedAppVersion != heartbeat.CachedAppVersion
                    || info.MetricsScheduleRevision != heartbeat.MetricsScheduleRevision
                    || info.AlertSettingsRevision != heartbeat.AlertSettingsRevision
                    || info.UtcOffsetMinutes != heartbeat.UtcOffsetMinutes
                    || info.PowerSource != heartbeat.PowerSource;

                entry.Online = true;
                entry.LastSeen = time.GetUtcNow();
                info.SignedInUser = heartbeat.SignedInUser;
                info.SessionRunning = heartbeat.SessionRunning;
                info.SessionId = heartbeat.SessionId;
                info.CachedAppVersion = heartbeat.CachedAppVersion;
                info.MetricsScheduleRevision = heartbeat.MetricsScheduleRevision;
                info.AlertSettingsRevision = heartbeat.AlertSettingsRevision;
                info.UtcOffsetMinutes = heartbeat.UtcOffsetMinutes;
                info.PowerSource = heartbeat.PowerSource;

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

        /// <summary>
        /// Forwards an RPCD update to that computer's agent and waits for its first snapshot.
        /// <paramref name="approved"/> is forwarded as given: the relay never turns it on.
        /// The fleet agent mints its own request id; the snapshot it sends back is the one returned.
        /// </summary>
        public async Task<UpdateRpcdOutcome> UpdateRpcdAsync(
            string machineId,
            string? channel,
            string? version,
            bool approved,
            string? approvedBy,
            bool adminKeyConfigured,
            bool adminKeyValid,
            string? groupScope,
            CancellationToken cancellationToken)
        {
            PendingUpdateAck? ack;
            string machineKey;
            lock (gate)
            {
                var begun = BeginUpdateNoLock(machineId, channel, version, approved, approvedBy, adminKeyConfigured, adminKeyValid, groupScope);
                if (begun.Ack == null)
                    return begun.Outcome!;
                ack = begun.Ack;
                machineKey = begun.MachineId;
            }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(UpdateAckTimeout);
            try
            {
                var progress = await ack.Done.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                return new UpdateRpcdOutcome(progress, ack.MachineName);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                AbandonAck(machineKey, ack);
                return new UpdateRpcdOutcome(null, ack.MachineName, TooOld: true, Error: TooOldForUpdateError);
            }
            catch (OperationCanceledException)
            {
                AbandonAck(machineKey, ack);
                throw;
            }
        }

        /// <summary>
        /// Opens a watch of one update, or of the latest when <paramref name="requestId"/> is empty.
        /// Replays the stored snapshot first and asks the agent for the copy it has now (empty request id means latest).
        /// </summary>
        public UpdateWatch OpenUpdateWatch(string machineId, string? requestId, bool adminKeyConfigured, bool adminKeyValid, string? groupScope)
        {
            lock (gate)
            {
                var decision = ConsentPolicy.Decide(false, ConsentMode.ConsentAlwaysAsk, adminKeyConfigured, adminKeyValid);
                if (!decision.Allowed)
                    return new UpdateWatch(null, Denied: true, Error: decision.Error ?? ConsentPolicy.AdminKeyRequired);

                if (!TryVisibleAgentNoLock(machineId, groupScope, out var entry, out var missing))
                    return new UpdateWatch(null, NotFound: true, Error: missing);

                bool specific = !string.IsNullOrEmpty(requestId);
                UpdateEntry? update = null;
                if (specific)
                {
                    if (updates.TryGetValue(requestId!, out var found))
                    {
                        if (!string.Equals(found.MachineId, entry.MachineId, StringComparison.OrdinalIgnoreCase))
                            return new UpdateWatch(null, NotFound: true, Error: UnknownAgentError);
                        update = found;
                    }
                }
                else if (!string.IsNullOrEmpty(entry.LatestUpdateRequestId)
                    && updates.TryGetValue(entry.LatestUpdateRequestId, out var latest))
                    update = latest;

                bool replayed = update != null && !string.IsNullOrEmpty(update.Latest.Stage);
                bool online = entry.Online && entry.ConnectionId != 0;
                if (!replayed && !online)
                    return new UpdateWatch(null, Offline: true, Error: OfflineError);

                var channel = Channel.CreateUnbounded<UpdateRpcdProgress>();
                if (replayed)
                    channel.Writer.TryWrite(update!.Latest);
                if (replayed && IsUpdateTerminal(update!.Latest))
                {
                    channel.Writer.TryComplete();
                    return new UpdateWatch(new UpdateSubscription(channel.Reader, replayed: true, () => { }));
                }

                if (specific)
                {
                    update ??= new UpdateEntry(requestId!, entry.MachineId) { Group = entry.Group, Updated = time.GetUtcNow() };
                    updates[update.RequestId] = update;
                    update.Subscribers.Add(channel);
                }
                else
                    entry.UpdateWatchers.Add(channel);

                // Ask even when the stream is down: the command waits on the agent and is sent when it reconnects.
                QueueUpdateStatusNoLock(entry, specific ? requestId! : string.Empty);
                string watchedId = update?.RequestId ?? string.Empty;
                return new UpdateWatch(new UpdateSubscription(channel.Reader, replayed, () =>
                {
                    lock (gate)
                    {
                        if (specific)
                        {
                            if (updates.TryGetValue(watchedId, out var current))
                                current.Subscribers.Remove(channel);
                        }
                        else
                            entry.UpdateWatchers.Remove(channel);
                    }
                    channel.Writer.TryComplete();
                }));
            }
        }

        /// <summary>Stores an update snapshot from an agent and fans it out. Returns the stored copy, or null when it was ignored.</summary>
        public UpdateRpcdProgress? OnUpdateProgress(AgentConnection connection, UpdateRpcdProgress progress)
        {
            ArgumentNullException.ThrowIfNull(progress);
            lock (gate)
            {
                if (!TryGetCurrentNoLock(connection, out var entry))
                    return null;

                entry.Online = true;
                entry.LastSeen = time.GetUtcNow();
                var stored = progress.Clone();

                // A status poll with no request id (nothing in progress). It must not replace a real update.
                if (string.IsNullOrEmpty(stored.RequestId))
                {
                    DeliverLatestNoLock(entry, stored);
                    return stored;
                }

                if (!updates.TryGetValue(stored.RequestId, out var update))
                {
                    update = new UpdateEntry(stored.RequestId, entry.MachineId) { Group = entry.Group };
                    updates[stored.RequestId] = update;
                }
                else if (!string.Equals(update.MachineId, entry.MachineId, StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }

                if (IsUpdateTerminal(update.Latest))
                    return null;

                bool newerThanLatest = string.IsNullOrEmpty(entry.LatestUpdateRequestId)
                    || string.Equals(entry.LatestUpdateRequestId, stored.RequestId, StringComparison.OrdinalIgnoreCase)
                    || !updates.TryGetValue(entry.LatestUpdateRequestId, out var currentLatest)
                    || IsUpdateTerminal(currentLatest.Latest);

                update.Latest = stored;
                update.Updated = time.GetUtcNow();
                if (newerThanLatest)
                    entry.LatestUpdateRequestId = stored.RequestId;

                foreach (var subscriber in update.Subscribers)
                    subscriber.Writer.TryWrite(stored);
                if (newerThanLatest)
                    DeliverLatestNoLock(entry, stored);

                if (IsUpdateTerminal(stored))
                {
                    foreach (var subscriber in update.Subscribers)
                        subscriber.Writer.TryComplete();
                    update.Subscribers.Clear();
                    if (newerThanLatest)
                        CompleteLatestWatchersNoLock(entry);
                }

                // The agent generates the request id. The first new, non-idle snapshot is the answer to the command we just sent.
                var ack = entry.PendingAck;
                if (ack != null
                    && !string.Equals(stored.Stage, "idle", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(stored.RequestId, ack.IgnoreRequestId, StringComparison.OrdinalIgnoreCase))
                {
                    entry.PendingAck = null;
                    ack.Done.TrySetResult(stored);
                }

                return stored;
            }
        }

        public UpdateRpcdProgress? GetLatestUpdate(string machineId)
        {
            lock (gate)
            {
                if (string.IsNullOrWhiteSpace(machineId)
                    || !agents.TryGetValue(machineId, out var entry)
                    || string.IsNullOrEmpty(entry.LatestUpdateRequestId)
                    || !updates.TryGetValue(entry.LatestUpdateRequestId, out var update))
                    return null;
                return update.Latest.Clone();
            }
        }

        /// <summary>Launch conditions with values kept in range (minutes of the day 0 to 1439, idle 0 to 240), or null when none is set.</summary>
        public static LaunchConditions? Clean(LaunchConditions? c)
        {
            if (c is null)
                return null;
            var clean = new LaunchConditions
            {
                RequireAcPower = c.RequireAcPower,
                MinIdleMinutes = Math.Clamp(c.MinIdleMinutes, 0, 240),
                WindowStartMinute = Math.Clamp(c.WindowStartMinute, 0, 1439),
                WindowEndMinute = Math.Clamp(c.WindowEndMinute, 0, 1439),
            };
            return clean.RequireAcPower || clean.MinIdleMinutes > 0 || clean.WindowStartMinute != clean.WindowEndMinute ? clean : null;
        }

        public StartSessionOutcome StartSession(string machineId, bool unattended, string requester, string serverUrl, bool adminKeyConfigured, bool adminKeyValid, string? groupScope = null, string? requesterUserId = null, bool hidden = false, LaunchConditions? conditions = null)
        {
            lock (gate)
            {
                AgentEntry? entry = null;
                if (!string.IsNullOrWhiteSpace(machineId))
                    agents.TryGetValue(machineId, out entry);
                if (entry != null && groupScope != null && !string.Equals(entry.Group, groupScope, StringComparison.OrdinalIgnoreCase))
                    entry = null;

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
                    Group = entry.Group,
                    StartedBy = requesterUserId ?? string.Empty,
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
                        Requester = requester ?? string.Empty,
                        RequireConsent = requireConsent,
                        Group = entry.Group,
                        // No window only when nobody is asked: a consent prompt needs the person to see the app.
                        Hidden = hidden && !requireConsent,
                        // Conditions only make sense for a hidden run: a session someone asked for at the PC starts at once.
                        Conditions = hidden && !requireConsent ? Clean(conditions) : null,
                    }
                });

                return new StartSessionOutcome(new StartSessionResponse
                {
                    Accepted = true,
                    RequestId = requestId,
                    SessionId = sessionId,
                    // The session is shared: the app's own window at the PC holds /app, and the requester joins at the
                    // read-only link and is given control there (the agent starts the app unattended).
                    AppUrl = baseUrl + "/mirror/" + sessionId,
                }, entry.Info.MachineName, requireConsent);
            }

            static StartSessionOutcome Refused(string error, string machineName) =>
                new(new StartSessionResponse { Accepted = false, Error = error }, machineName, true);
        }

        /// <summary>
        /// The signed-in user id of whoever asked for the session with this id, or null when the relay started no such session
        /// or it was asked for without sign-in. That user is the one given control (SessionControllers).
        /// </summary>
        public string? StartedBy(string sessionId)
        {
            if (string.IsNullOrEmpty(sessionId))
                return null;
            lock (gate)
            {
                foreach (var launch in launches.Values)
                    if (launch.SessionId == sessionId && !string.IsNullOrEmpty(launch.StartedBy))
                        return launch.StartedBy;
            }
            return null;
        }

        public CancelLaunchOutcome CancelLaunch(string requestId, string? groupScope = null)
        {
            lock (gate)
            {
                if (string.IsNullOrEmpty(requestId) || !launches.TryGetValue(requestId, out var launch))
                    return CancelLaunchOutcome.NotFound;
                if (groupScope != null && !string.Equals(launch.Group, groupScope, StringComparison.OrdinalIgnoreCase))
                    return CancelLaunchOutcome.NotFound;
                if (IsFinal(launch.Latest.Stage))
                    return CancelLaunchOutcome.AlreadyFinished;
                if (!agents.TryGetValue(launch.MachineId, out var entry))
                    return CancelLaunchOutcome.NotFound;

                entry.Commands.Writer.TryWrite(new HubCommand { Cancel = new CancelCommand { RequestId = requestId } });
                return CancelLaunchOutcome.Queued;
            }
        }

        /// <summary>
        /// Stops what the agent started on a computer: the running app, or a start still in progress. The agent already ends a launch
        /// on a cancel for its request ID (it closes the app and reports ENDED), so this finds the computer's newest unfinished launch
        /// and sends that cancel. A session the relay has no launch for cannot be stopped this way.
        /// </summary>
        public EndSessionResult EndSession(string machineId, string? groupScope = null) => FindEnd(machineId, groupScope, send: true);

        /// <summary>
        /// What <see cref="EndSession"/> would do right now, without sending anything: lets the caller end the relay's side of the
        /// session (with a reason) before the agent closes the app, so the app's streams are not torn down under a live session.
        /// </summary>
        public EndSessionResult PeekEndSession(string machineId, string? groupScope = null) => FindEnd(machineId, groupScope, send: false);

        private EndSessionResult FindEnd(string machineId, string? groupScope, bool send)
        {
            lock (gate)
            {
                AgentEntry? entry = null;
                if (!string.IsNullOrWhiteSpace(machineId))
                    agents.TryGetValue(machineId, out entry);
                if (entry == null || (groupScope != null && !string.Equals(entry.Group, groupScope, StringComparison.OrdinalIgnoreCase)))
                    return new EndSessionResult(EndSessionOutcome.EndSessionNotFound);

                LaunchEntry? live = null;
                foreach (var launch in launches.Values)
                {
                    if (!string.Equals(launch.MachineId, entry.MachineId, StringComparison.OrdinalIgnoreCase) || IsFinal(launch.Latest.Stage))
                        continue;
                    if (live == null || launch.Updated > live.Updated)
                        live = launch;
                }

                if (live == null)
                    return new EndSessionResult(
                        entry.Info.SessionRunning ? EndSessionOutcome.EndSessionNotAgentLaunched : EndSessionOutcome.EndSessionNotRunning,
                        MachineName: entry.Info.MachineName);
                if (!entry.Online || entry.ConnectionId == 0)
                    return new EndSessionResult(EndSessionOutcome.EndSessionOffline, MachineName: entry.Info.MachineName);

                if (send)
                    entry.Commands.Writer.TryWrite(new HubCommand { Cancel = new CancelCommand { RequestId = live.RequestId } });
                return new EndSessionResult(EndSessionOutcome.EndSessionRequested, live.RequestId, entry.Info.MachineName, live.SessionId);
            }
        }

        /// <summary>Replays the latest stage, then live updates; the reader completes after a terminal stage. Null when unknown.</summary>
        public LaunchSubscription? SubscribeLaunch(string requestId, string? groupScope = null)
        {
            lock (gate)
            {
                if (string.IsNullOrEmpty(requestId) || !launches.TryGetValue(requestId, out var launch))
                    return null;
                if (groupScope != null && !string.Equals(launch.Group, groupScope, StringComparison.OrdinalIgnoreCase))
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

        public AgentInfo? GetAgent(string machineId, string? groupScope = null)
        {
            lock (gate)
            {
                if (!agents.TryGetValue(machineId, out var entry))
                    return null;
                if (groupScope != null && !string.Equals(entry.Group, groupScope, StringComparison.OrdinalIgnoreCase))
                    return null;
                return SnapshotNoLock(entry);
            }
        }

        public AgentList GetAgentList(string? groupScope = null)
        {
            var list = new AgentList();
            lock (gate)
            {
                IEnumerable<AgentEntry> source = agents.Values;
                if (groupScope != null)
                    source = source.Where(entry => string.Equals(entry.Group, groupScope, StringComparison.OrdinalIgnoreCase));
                list.Agents.AddRange(source
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
                SweepUpdatesNoLock(now);

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

        private void SweepUpdatesNoLock(DateTimeOffset now)
        {
            foreach (var update in updates.Values.ToList())
            {
                if (now - update.Updated < ForgetLaunchAfter)
                    continue;
                foreach (var subscriber in update.Subscribers)
                    subscriber.Writer.TryComplete();
                updates.Remove(update.RequestId);
                if (agents.TryGetValue(update.MachineId, out var entry)
                    && string.Equals(entry.LatestUpdateRequestId, update.RequestId, StringComparison.OrdinalIgnoreCase))
                    entry.LatestUpdateRequestId = null;
            }
        }

        private BegunUpdate BeginUpdateNoLock(
            string machineId,
            string? channel,
            string? version,
            bool approved,
            string? approvedBy,
            bool adminKeyConfigured,
            bool adminKeyValid,
            string? groupScope)
        {
            if (!TryVisibleAgentNoLock(machineId, groupScope, out var entry, out var missing))
            {
                // The admin key is checked before anything about the computer is revealed.
                var hidden = ConsentPolicy.DecideUpdate(adminKeyConfigured, adminKeyValid);
                if (!hidden.Allowed)
                    return BegunUpdate.Finished(new UpdateRpcdOutcome(null, Denied: true, Error: hidden.Error ?? ConsentPolicy.AdminKeyRequired));
                return BegunUpdate.Finished(new UpdateRpcdOutcome(null, NotFound: true, Error: missing));
            }

            var decision = ConsentPolicy.DecideUpdate(adminKeyConfigured, adminKeyValid);
            if (!decision.Allowed)
                return BegunUpdate.Finished(new UpdateRpcdOutcome(null, Denied: true, Error: decision.Error ?? ConsentPolicy.AdminKeyRequired));

            if (!entry.Online || entry.ConnectionId == 0)
                return BegunUpdate.Finished(new UpdateRpcdOutcome(null, entry.Info.MachineName, Offline: true, Error: OfflineError));

            if (entry.PendingAck != null)
            {
                return BegunUpdate.Finished(new UpdateRpcdOutcome(
                    RefusedUpdate(UpdateInProgressError),
                    entry.Info.MachineName));
            }

            var ack = new PendingUpdateAck(entry.Info.MachineName, entry.LatestUpdateRequestId);
            entry.PendingAck = ack;
            // approved is copied as the caller sent it. Nothing here sets it true.
            entry.Commands.Writer.TryWrite(new HubCommand
            {
                UpdateRpcd = new UpdateRpcdCommand
                {
                    RequestId = Guid.NewGuid().ToString(),
                    Channel = channel?.Trim() ?? string.Empty,
                    Version = version?.Trim() ?? string.Empty,
                    Approved = approved,
                    ApprovedBy = approvedBy?.Trim() ?? string.Empty,
                }
            });
            return new BegunUpdate(null, ack, entry.MachineId);
        }

        private bool TryVisibleAgentNoLock(string machineId, string? groupScope, out AgentEntry entry, out string error)
        {
            entry = null!;
            if (string.IsNullOrWhiteSpace(machineId))
            {
                error = NoAgentError;
                return false;
            }
            if (!agents.TryGetValue(machineId, out var found)
                || (groupScope != null && !string.Equals(found.Group, groupScope, StringComparison.OrdinalIgnoreCase)))
            {
                error = UnknownAgentError;
                return false;
            }
            entry = found;
            error = string.Empty;
            return true;
        }

        private void AbandonAck(string machineId, PendingUpdateAck ack)
        {
            lock (gate)
            {
                if (agents.TryGetValue(machineId, out var entry) && ReferenceEquals(entry.PendingAck, ack))
                    entry.PendingAck = null;
            }
        }

        private static void QueueUpdateStatusNoLock(AgentEntry entry, string requestId) =>
            entry.Commands.Writer.TryWrite(new HubCommand { UpdateRpcdStatus = new UpdateRpcdStatusCommand { RequestId = requestId ?? string.Empty } });

        private static void DeliverLatestNoLock(AgentEntry entry, UpdateRpcdProgress stored)
        {
            foreach (var watcher in entry.UpdateWatchers)
                watcher.Writer.TryWrite(stored);
        }

        private static void CompleteLatestWatchersNoLock(AgentEntry entry)
        {
            foreach (var watcher in entry.UpdateWatchers)
                watcher.Writer.TryComplete();
            entry.UpdateWatchers.Clear();
        }

        private static UpdateRpcdProgress RefusedUpdate(string error) => new()
        {
            Stage = "refused",
            Terminal = true,
            Error = error,
            Detail = error,
        };

        private readonly struct BegunUpdate(UpdateRpcdOutcome? outcome, PendingUpdateAck? ack, string machineId)
        {
            public UpdateRpcdOutcome? Outcome { get; } = outcome;
            public PendingUpdateAck? Ack { get; } = ack;
            public string MachineId { get; } = machineId;
            public static BegunUpdate Finished(UpdateRpcdOutcome outcome) => new(outcome, null, string.Empty);
        }

        private sealed class PendingUpdateAck(string machineName, string? ignoreRequestId)
        {
            public TaskCompletionSource<UpdateRpcdProgress> Done { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
            public string MachineName { get; } = machineName;
            public string? IgnoreRequestId { get; } = ignoreRequestId;
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
            info.Group = entry.Group;
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
            public string Group { get; set; } = string.Empty;
            public byte[]? AgentTokenHash { get; set; }
            public long ConnectionId { get; set; }
            public CancellationTokenSource? ConnectionCts { get; set; }
            public IServerStreamWriter<HubCommand>? Writer { get; set; }
            // Per agent, not per stream: a reconnecting agent picks up what was queued for it.
            public Channel<HubCommand> Commands { get; } = Channel.CreateUnbounded<HubCommand>();
            public PendingUpdateAck? PendingAck { get; set; }
            public string? LatestUpdateRequestId { get; set; }
            public List<Channel<UpdateRpcdProgress>> UpdateWatchers { get; } = [];
        }

        private sealed class UpdateEntry(string requestId, string machineId)
        {
            public string RequestId { get; } = requestId;
            public string MachineId { get; } = machineId;
            public string Group { get; set; } = string.Empty;
            public UpdateRpcdProgress Latest { get; set; } = new();
            public DateTimeOffset Updated { get; set; }
            public List<Channel<UpdateRpcdProgress>> Subscribers { get; } = [];
        }

        private sealed class LaunchEntry(string requestId, string machineId, string sessionId)
        {
            public string RequestId { get; } = requestId;
            public string MachineId { get; } = machineId;
            public string SessionId { get; } = sessionId;
            public string Group { get; set; } = string.Empty;
            /// <summary>The signed-in user id of whoever asked for the session (empty without sign-in). See <see cref="StartedBy(string)"/>.</summary>
            public string StartedBy { get; init; } = string.Empty;
            public LaunchProgress Latest { get; set; } = new();
            public DateTimeOffset Updated { get; set; }
            public List<Channel<LaunchProgress>> Subscribers { get; } = [];
        }
    }
}
