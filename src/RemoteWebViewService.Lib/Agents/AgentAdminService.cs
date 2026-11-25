using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using PeakSWC.RemoteWebView.Agent;
using PeakSWC.RemoteWebView.Services;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace PeakSWC.RemoteWebView.Agents
{
    /// <summary>AgentAdmin: what the admin site (gRPC-Web) uses to list computers and start diagnostics on one.</summary>
    [System.Diagnostics.CodeAnalysis.Experimental(ExperimentalApi.AgentRegistry, UrlFormat = ExperimentalApi.UrlFormat)]
    public class AgentAdminService(ILogger<AgentAdminService> logger, AgentRegistry registry, AgentOptions options, PeakSWC.RemoteWebView.IAgentTenantPolicy? tenantPolicy = null, ShutdownService? shutdownService = null) : AgentAdmin.AgentAdminBase
    {
        private const string Category = "PeakSWC.RemoteWebView.Agents.AgentAdminService";
        private readonly PeakSWC.RemoteWebView.IAgentTenantPolicy policy = tenantPolicy ?? new PeakSWC.RemoteWebView.AllowAllAgentTenantPolicy();
        public const string DefaultRequester = "Someone";
        public static readonly TimeSpan WatchAgentsInterval = TimeSpan.FromSeconds(15);
        private static int warnedNoAdminKey;

        public override async Task<AgentList> GetAgents(Empty request, ServerCallContext context)
        {
            var access = await RequireAccessAsync(context).ConfigureAwait(false);
            return registry.GetAgentList(access.GroupScope);
        }

        public override async Task WatchAgents(Empty request, IServerStreamWriter<AgentList> responseStream, ServerCallContext context)
        {
            var token = context.CancellationToken;
            try
            {
                while (!token.IsCancellationRequested)
                {
                    // Take the signal before the snapshot so a change in between still wakes us.
                    var changed = registry.Changed;
                    var access = await RequireAccessAsync(context).ConfigureAwait(false);
                    await responseStream.WriteAsync(registry.GetAgentList(access.GroupScope), token).ConfigureAwait(false);
                    try
                    {
                        await changed.WaitAsync(WatchAgentsInterval, token).ConfigureAwait(false);
                    }
                    catch (TimeoutException)
                    {
                        // The interval elapsed with no registry change. Loop and send a fresh snapshot.
                    }
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                // The admin client disconnected.
            }
        }

        public override async Task WatchMetrics(Empty request, IServerStreamWriter<MetricsUpload> responseStream, ServerCallContext context)
        {
            var access = await RequireAccessAsync(context).ConfigureAwait(false);
            RequireMutation(access, context);
            using var subscription = registry.SubscribeMetrics(access.GroupScope);
            var token = context.CancellationToken;
            try
            {
                await foreach (var upload in subscription.Reader.ReadAllAsync(token).ConfigureAwait(false))
                    await responseStream.WriteAsync(upload, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                // The admin client disconnected; unacknowledged runs stay in the agents' outboxes.
            }
        }

        public override async Task<Empty> AckMetrics(MetricsAckRequest request, ServerCallContext context)
        {
            var access = await RequireAccessAsync(context).ConfigureAwait(false);
            RequireMutation(access, context);
            // An offline agent keeps the runs and uploads them again; Admin saves by run id, so that is never a duplicate.
            registry.AckMetrics(request.MachineId, request.ThroughSeq, access.GroupScope, out _);
            return new Empty();
        }

        public override async Task<MetricsCommandResponse> SetMetricsSchedule(MetricsScheduleRequest request, ServerCallContext context)
        {
            var access = await RequireAccessAsync(context).ConfigureAwait(false);
            RequireMutation(access, context);
            if (request.Schedule == null)
                throw new RpcException(new Status(StatusCode.InvalidArgument, "schedule is required"));
            bool sent = registry.SetMetricsSchedule(request.MachineId, request.Schedule, access.GroupScope, out var error);
            if (sent)
                AgentEvents.Info(logger, Category, $"Scheduled metrics set on {request.MachineId}: revision {request.Schedule.Revision}, " +
                    (request.Schedule.Enabled ? $"every {request.Schedule.IntervalMinutes} min" : "off"));
            return new MetricsCommandResponse { Sent = sent, Error = sent ? string.Empty : error };
        }

        public override async Task<MetricsCommandResponse> CollectMetrics(CollectMetricsRequest request, ServerCallContext context)
        {
            var access = await RequireAccessAsync(context).ConfigureAwait(false);
            RequireMutation(access, context);
            bool sent = registry.CollectMetrics(request.MachineId, access.GroupScope, out var error);
            return new MetricsCommandResponse { Sent = sent, Error = sent ? string.Empty : error };
        }

        /// <summary>
        /// A caller that isn't scoped to a shop needs the admin key when one is configured (the rule CancelLaunch uses), unless its ticket
        /// already says it may act for every shop: the admin site's own service identity, or a full admin.
        /// </summary>
        private void RequireMutation(PeakSWC.RemoteWebView.AgentAdminAccessDecision access, ServerCallContext context)
        {
            if (access.GroupScope != null || access.UnattendedAuthorized || access.CallerRole is "service" or "admin")
                return;
            if (options.AdminKeyConfigured && !options.IsAdminKeyValid(GetAdminKey(context)))
                throw new RpcException(new Status(StatusCode.PermissionDenied, ConsentPolicy.AdminKeyRequired));
        }

        public override async Task<StartSessionResponse> StartSession(StartSessionRequest request, ServerCallContext context)
        {
            var access = await RequireAccessAsync(context).ConfigureAwait(false);
            string requester = GetRequester(request.Requester, context);
            string serverUrl = GetServerUrl(options, context);
            var (adminKeyConfigured, adminKeyValid) = MutationAuthority(access, context);

            var outcome = registry.StartSession(request.MachineId, request.Unattended, requester, serverUrl, adminKeyConfigured, adminKeyValid, access.GroupScope, RequesterUserId(context), request.Hidden);
            var response = outcome.Response;
            if (outcome.Denied)
            {
                AgentEvents.Warn(logger, $"Diagnostics request on {request.MachineId} refused: missing or wrong {AgentOptions.AdminKeyHeader}");
                throw new RpcException(new Status(StatusCode.PermissionDenied, response.Error));
            }
            string who = string.IsNullOrEmpty(outcome.MachineName) ? request.MachineId : $"{outcome.MachineName} ({request.MachineId})";
            string actor = string.IsNullOrWhiteSpace(access.CallerRole) ? requester : $"{requester} ({access.CallerRole})";

            if (response.Accepted)
                AgentEvents.Info(logger, Category,
                    $"Diagnostics requested on {who} by {actor}: request {response.RequestId}, session {response.SessionId}, " +
                    (outcome.RequireConsent ? "consent required" : "unattended"));
            else
                AgentEvents.Info(logger, Category, $"Diagnostics request on {who} by {actor} refused: {response.Error}");

            return response;
        }

        public override async Task WatchLaunch(WatchLaunchRequest request, IServerStreamWriter<LaunchProgress> responseStream, ServerCallContext context)
        {
            var access = await RequireAccessAsync(context).ConfigureAwait(false);
            using var subscription = registry.SubscribeLaunch(request.RequestId, access.GroupScope)
                ?? throw new RpcException(new Status(StatusCode.NotFound, "No launch with that request_id"));
            var token = context.CancellationToken;
            try
            {
                await foreach (var progress in subscription.Reader.ReadAllAsync(token).ConfigureAwait(false))
                    await responseStream.WriteAsync(progress, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                // The admin client disconnected.
            }
        }

        public override async Task<Empty> CancelLaunch(CancelCommand request, ServerCallContext context)
        {
            var access = await RequireAccessAsync(context).ConfigureAwait(false);
            if (access.GroupScope == null && options.AdminKeyConfigured && !options.IsAdminKeyValid(GetAdminKey(context)))
                throw new RpcException(new Status(StatusCode.PermissionDenied, ConsentPolicy.AdminKeyRequired));

            var outcome = registry.CancelLaunch(request.RequestId, access.GroupScope);
            if (outcome == CancelLaunchOutcome.NotFound)
                throw new RpcException(new Status(StatusCode.NotFound, "No launch with that request_id"));
            if (outcome == CancelLaunchOutcome.Queued)
                AgentEvents.Info(logger, Category, $"Cancel requested for launch {request.RequestId}");
            return new Empty();
        }

        public override async Task<EndSessionResponse> EndSession(EndSessionRequest request, ServerCallContext context)
        {
            var access = await RequireAccessAsync(context).ConfigureAwait(false);
            if (access.GroupScope == null && options.AdminKeyConfigured && !options.IsAdminKeyValid(GetAdminKey(context)))
                throw new RpcException(new Status(StatusCode.PermissionDenied, ConsentPolicy.AdminKeyRequired));

            string requester = GetRequester(request.Requester, context);

            // End the relay's side first, with a reason, then tell the agent to close the app. The agent closes it within about half
            // a second and its HTTP/2 connection faults; with the session already ended, the app's streams stop on the session's own
            // cancellation and the late aborts log at Debug instead of as failures.
            var planned = registry.PeekEndSession(request.MachineId, access.GroupScope);
            if (planned.Outcome == EndSessionOutcome.EndSessionRequested && shutdownService != null && !string.IsNullOrEmpty(planned.SessionId))
                await shutdownService.Shutdown(planned.SessionId, SessionEndReason.EndRequestedByAdmin, detail: "by " + requester).ConfigureAwait(false);

            var result = registry.EndSession(request.MachineId, access.GroupScope);
            if (result.Outcome == EndSessionOutcome.EndSessionRequested)
            {
                string who = string.IsNullOrEmpty(result.MachineName) ? request.MachineId : $"{result.MachineName} ({request.MachineId})";
                AgentEvents.Info(logger, Category, $"Diagnostics end requested on {who} by {requester}: request {result.RequestId}");
            }
            return new EndSessionResponse { Outcome = result.Outcome, RequestId = result.RequestId };
        }

        public override async Task<Empty> PublishRelease(ReleaseAvailableCommand request, ServerCallContext context)
        {
            var access = await RequireAccessAsync(context).ConfigureAwait(false);
            if (access.GroupScope != null)
                throw new RpcException(new Status(StatusCode.PermissionDenied, "Global admin access is required to publish releases."));
            if (!access.UnattendedAuthorized && options.AdminKeyConfigured && !options.IsAdminKeyValid(GetAdminKey(context)))
                throw new RpcException(new Status(StatusCode.PermissionDenied, ConsentPolicy.AdminKeyRequired));

            int notified = registry.PublishRelease(request);
            AgentEvents.Info(logger, Category,
                $"Release announcement published (stable='{request.StableVersion}', prerelease='{request.PrereleaseVersion}'): notified {notified} online agent(s)");
            return new Empty();
        }

        public override async Task<UpdateRpcdProgress> UpdateRpcd(UpdateRpcdAdminRequest request, ServerCallContext context)
        {
            var access = await RequireAccessAsync(context).ConfigureAwait(false);
            var (adminKeyConfigured, adminKeyValid) = MutationAuthority(access, context);
            var outcome = await registry.UpdateRpcdAsync(
                request.MachineId,
                request.Channel,
                request.Version,
                request.Approved,
                request.ApprovedBy,
                adminKeyConfigured,
                adminKeyValid,
                access.GroupScope,
                context.CancellationToken).ConfigureAwait(false);

            if (outcome.Denied)
            {
                AgentEvents.Warn(logger, $"RPCD update on {request.MachineId} refused: missing or wrong {AgentOptions.AdminKeyHeader}");
                throw new RpcException(new Status(StatusCode.PermissionDenied, outcome.Error ?? ConsentPolicy.AdminKeyRequired));
            }

            string who = string.IsNullOrEmpty(outcome.MachineName) ? request.MachineId : $"{outcome.MachineName} ({request.MachineId})";
            if (outcome.NotFound)
                throw new RpcException(new Status(StatusCode.NotFound, outcome.Error ?? AgentRegistry.UnknownAgentError));
            if (outcome.Offline)
                throw new RpcException(new Status(StatusCode.FailedPrecondition, outcome.Error ?? AgentRegistry.OfflineError));
            if (outcome.TooOld)
            {
                AgentEvents.Warn(logger, $"RPCD update on {who} refused: {AgentRegistry.TooOldForUpdateError}");
                throw new RpcException(new Status(StatusCode.FailedPrecondition, outcome.Error ?? AgentRegistry.TooOldForUpdateError));
            }

            var progress = outcome.Progress ?? throw new RpcException(new Status(StatusCode.Internal, "The update did not return a status"));
            AgentEvents.Info(logger, Category,
                $"RPCD update on {who}: channel '{request.Channel}', version '{request.Version}', approved {request.Approved}, stage {progress.Stage}, request {progress.RequestId}");
            return progress;
        }

        public override async Task WatchUpdateRpcd(UpdateRpcdWatchRequest request, IServerStreamWriter<UpdateRpcdProgress> responseStream, ServerCallContext context)
        {
            var access = await RequireAccessAsync(context).ConfigureAwait(false);
            var (adminKeyConfigured, adminKeyValid) = MutationAuthority(access, context);
            var opened = registry.OpenUpdateWatch(request.MachineId, request.RequestId, adminKeyConfigured, adminKeyValid, access.GroupScope);
            if (opened.Denied)
                throw new RpcException(new Status(StatusCode.PermissionDenied, opened.Error ?? ConsentPolicy.AdminKeyRequired));
            if (opened.NotFound)
                throw new RpcException(new Status(StatusCode.NotFound, opened.Error ?? AgentRegistry.UnknownAgentError));
            if (opened.Offline)
                throw new RpcException(new Status(StatusCode.FailedPrecondition, opened.Error ?? AgentRegistry.OfflineError));

            var subscription = opened.Subscription ?? throw new RpcException(new Status(StatusCode.Internal, "The update watch did not open"));
            using var _ = subscription;
            var token = context.CancellationToken;
            try
            {
                if (!subscription.Replayed)
                {
                    using var ack = CancellationTokenSource.CreateLinkedTokenSource(token);
                    ack.CancelAfter(registry.UpdateAckTimeout);
                    try
                    {
                        if (!await subscription.Reader.WaitToReadAsync(ack.Token).ConfigureAwait(false))
                            throw new RpcException(new Status(StatusCode.FailedPrecondition, AgentRegistry.TooOldForUpdateError));
                    }
                    catch (OperationCanceledException) when (!token.IsCancellationRequested)
                    {
                        throw new RpcException(new Status(StatusCode.FailedPrecondition, AgentRegistry.TooOldForUpdateError));
                    }
                }

                await foreach (var progress in subscription.Reader.ReadAllAsync(token).ConfigureAwait(false))
                    await responseStream.WriteAsync(progress, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                // The admin client disconnected.
            }
        }

        /// <summary>
        /// The same admin-key inputs <see cref="StartSession"/> uses. A relay ticket that may start a session unattended counts as the key.
        /// A shop-scoped caller who is not authorized for that is refused, the same as <see cref="StartSession"/>.
        /// </summary>
        private (bool Configured, bool Valid) MutationAuthority(PeakSWC.RemoteWebView.AgentAdminAccessDecision access, ServerCallContext context)
        {
            if (access.UnattendedAuthorized)
                return (true, true);
            if (access.GroupScope != null)
                return (true, false);
            if (!options.AdminKeyConfigured && Interlocked.Exchange(ref warnedNoAdminKey, 1) == 0)
                AgentEvents.Warn(logger, $"{AgentOptions.AdminKeyVariable} is not set: every diagnostics start asks the person at the PC for consent");
            return (options.AdminKeyConfigured, options.IsAdminKeyValid(GetAdminKey(context)));
        }

        private async Task<PeakSWC.RemoteWebView.AgentAdminAccessDecision> RequireAccessAsync(ServerCallContext context)
        {
            var access = await ResolveAccessAsync(context).ConfigureAwait(false);
            if (!access.Allowed)
                throw new RpcException(new Status(StatusCode.PermissionDenied, access.DenialReason ?? "Access denied"));
            return access;
        }

        private Task<PeakSWC.RemoteWebView.AgentAdminAccessDecision> ResolveAccessAsync(ServerCallContext context)
        {
            HttpContext? http = null;
            try { http = context.GetHttpContext(); }
            catch (InvalidOperationException) { }
            return policy.CheckAdminAccessAsync(http, GetAdminKey(context), options, context.CancellationToken);
        }

        /// <summary>The x-admin-key request header (gRPC metadata), or null.</summary>
        public static string? GetAdminKey(ServerCallContext context)
        {
            foreach (var entry in context.RequestHeaders)
            {
                if (!entry.IsBinary && string.Equals(entry.Key, AgentOptions.AdminKeyHeader, StringComparison.OrdinalIgnoreCase))
                    return entry.Value;
            }
            return null;
        }

        /// <summary>Longest label a caller may put after the signed-in name, not counting the parentheses.</summary>
        public const int MaxRequesterLabelLength = 32;

        /// <summary>
        /// The signed-in user's name when authentication is on, else what the admin site sent, else "Someone". With authentication on the
        /// caller cannot choose the name, only add a short label after it (see <see cref="WithLabel"/>).
        /// </summary>
        /// <param name="requested">The requester field the caller sent (StartSessionRequest or EndSessionRequest), or null.</param>
        /// <summary>The signed-in caller's user id, or null without sign-in. The session it starts gives control only to that user.</summary>
        private static string? RequesterUserId(ServerCallContext context) =>
            CallContexts.Http(context)?.User is { Identity.IsAuthenticated: true } user ? user.GetUserObjectId() : null;

        public static string GetRequester(string? requested, ServerCallContext context)
        {
            try
            {
                var user = context.GetHttpContext().User;
                if (user.Identity?.IsAuthenticated == true)
                {
                    string? name = user.FindFirst("name")?.Value ?? user.Identity.Name;
                    if (!string.IsNullOrWhiteSpace(name))
                        return WithLabel(name.Trim(), requested);
                }
            }
            catch (InvalidOperationException)
            {
                // Not hosted by ASP.NET Core (unit tests).
            }

            return string.IsNullOrWhiteSpace(requested) ? DefaultRequester : requested.Trim();
        }

        /// <summary>
        /// Keeps a label the caller put in parentheses after the signed-in name, so the consent prompt and the audit line can say how the
        /// session was asked for: "Pat Lee (AI connector)". Anything else the caller sent is dropped, so it can never show another
        /// person's name. The label is letters, digits, spaces and hyphens, at most <see cref="MaxRequesterLabelLength"/> characters.
        /// </summary>
        public static string WithLabel(string name, string? requested)
        {
            string text = requested?.Trim() ?? string.Empty;
            string prefix = name + " (";
            if (text.Length <= prefix.Length + 1 || !text.StartsWith(prefix, StringComparison.Ordinal) || text[^1] != ')')
                return name;

            string label = text[prefix.Length..^1].Trim();
            if (label.Length == 0 || label.Length > MaxRequesterLabelLength)
                return name;
            foreach (char ch in label)
            {
                if (!char.IsLetterOrDigit(ch) && ch != ' ' && ch != '-')
                    return name;
            }
            return $"{name} ({label})";
        }

        /// <summary>PUBLIC_BASE_URL when set, else the scheme and host this request came in on.</summary>
        public static string GetServerUrl(AgentOptions options, ServerCallContext context)
        {
            if (options.PublicBaseUrl != null)
                return options.PublicBaseUrl;

            try
            {
                var httpRequest = context.GetHttpContext().Request;
                if (httpRequest.Host.HasValue)
                    return $"{httpRequest.Scheme}://{httpRequest.Host.Value}";
            }
            catch (InvalidOperationException)
            {
                // Not hosted by ASP.NET Core (unit tests).
            }

            return "https://" + context.Host;
        }
    }
}
