using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using PeakSWC.RemoteWebView.EndPoints;
using PeakSWC.RemoteWebView.Services;
using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace PeakSWC.RemoteWebView
{
    public class BrowserIPCService(ILogger<RemoteWebViewService> logger, ConcurrentDictionary<string, TaskCompletionSource<ServiceState>> serviceDictionary, ShutdownService shutdownService, IRelayAuthorization? authorization = null, IUserService? users = null) : BrowserIPC.BrowserIPCBase
    {
        internal static readonly TimeSpan SessionPollInterval = TimeSpan.FromMilliseconds(250);

        /// <summary>Waits for an app to register <paramref name="id"/> without adding an entry for it. Null when none did in time.</summary>
        private async Task<PendingSession?> WaitForSessionAsync(string id, TimeSpan timeout, CancellationToken cancellationToken)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (true)
            {
                var pending = PendingSessions.TryJoin(serviceDictionary, id);
                if (pending != null)
                    return pending;
                if (DateTime.UtcNow >= deadline)
                    return null;
                try
                {
                    await Task.Delay(SessionPollInterval, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return null;
                }
            }
        }

        public override async Task ReceiveMessage(ReceiveMessageRequest request, IServerStreamWriter<StringRequest> responseStream, ServerCallContext context)
        {
            PendingSession? pending;
            if (authorization?.Enabled == true && CallContexts.Http(context)?.User?.Identity?.IsAuthenticated != true)
            {
                // Not signed in: only the owner's page may connect, and its token or cookie can be checked only once the app
                // has registered. Wait for the app's entry without adding one, so a caller that is not signed in adds nothing.
                pending = await WaitForSessionAsync(request.Id, TimeSpan.FromSeconds(60), context.CancellationToken).ConfigureAwait(false);
                if (pending == null)
                {
                    logger.LogWarning("Connection refused: not signed in to this session's group. Id: {Id}, ClientId: {ClientId}", request.Id, request.ClientId);
                    throw new RpcException(new Status(StatusCode.PermissionDenied, "Sign in to a group that may open this session."));
                }
            }
            else
            {
                pending = PendingSessions.Join(serviceDictionary, request.Id);
            }

            using var _ = pending;
            var serviceStateTaskSource = pending.Source;

            ServiceState? serviceState = null;
            bool attached = false;
            try
            {
                // Must not be context.CancellationToken: a disconnecting browser would cancel this wait and shut down early.
                serviceState = await serviceStateTaskSource.Task.WaitAsync(TimeSpan.FromSeconds(60), CancellationToken.None).ConfigureAwait(false);

                // With sign-in on, the same people who may open the session's pages: the owner's page, or a member of its group.
                if (!await BrowserCallAccess.AllowsAsync(context, serviceState, authorization, users, request.OwnerToken).ConfigureAwait(false))
                {
                    logger.LogWarning("Connection refused: not signed in to this session's group. Id: {Id}, ClientId: {ClientId}", request.Id, request.ClientId);
                    throw new RpcException(new Status(StatusCode.PermissionDenied, "Sign in to a group that may open this session."));
                }

                // A clientId that is already known belongs to the connection that holds its key. Without this, any browser
                // could open a stream under the controller's or the owner's clientId (the roster names both).
                attached = serviceState.ClientKeys.Attach(request.ClientId, request.ClientKey);
                if (!attached)
                {
                    logger.LogWarning("Connection refused: the clientId is in use with another key. Id: {Id}, ClientId: {ClientId}", request.Id, request.ClientId);
                    return;
                }

                // The machine owner's local control surface identifies itself (?role=owner). Only the owner may reclaim control.
                // When the app sent an owner key, the claim needs the owner token too (OwnerAccess); without it the
                // connection is an ordinary remote connection, whatever its page's address says.
                bool isOwner = OwnerAccess.AcceptsOwner(serviceState, request.IsOwner, request.OwnerToken);
                if (request.IsOwner && !isOwner)
                    logger.LogWarning("Owner claim without the owner token ignored. Id: {Id}, ClientId: {ClientId}", request.Id, request.ClientId);
                if (isOwner)
                    serviceState.OwnerClientId = request.ClientId;

                using var linkedToken = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken, serviceState.Token);

                // The signed-in name, for the roster and the app's audit (ParticipantsChanged). The page's sign-in cookie comes
                // with its gRPC-Web call when the relay serves both; otherwise (and without authorization) it is empty.
                var userName = SignedInName(context);
                serviceState.IPC.BrowserResponseStream(new BrowserResponseNode(responseStream, request.ClientId, request.IsPrimary) { UserName = userName, UserId = SignedInId(context) }, linkedToken);

                // The machine owner holds control from the start. The owner's surface may connect after the remote user
                // (in mirror mode it connects only once the remote user is present), so promote it on its first
                // connection even if the remote user already claimed control - but only once, so a later owner reconnect
                // does not grab control back. PromoteAsync also broadcasts the roster; otherwise just tell everyone present.
                await serviceState.BrowserIPC.ControlChange.WaitAsync(CancellationToken.None).ConfigureAwait(false);
                try
                {
                    if (isOwner && !serviceState.OwnerControlInitialized)
                    {
                        serviceState.OwnerControlInitialized = true;
                        var holder = serviceState.PrimaryClientId;
                        if (string.IsNullOrEmpty(holder) || holder == request.ClientId)
                        {
                            // The owner already is (or is first to be) the controller - e.g. its surface is the /app page, which
                            // sends its connect handshake before this stream opens. Do not reset the sequence: its messages are
                            // already numbered past 1 and a reset would strand everything it sends afterwards.
                            serviceState.PrimaryClientId = request.ClientId;
                            serviceState.IPC.SetPrimary(request.ClientId);
                            serviceState.IPC.BroadcastParticipants(serviceState.OwnerClientId, serviceState.PrimaryClientId);
                        }
                        else
                            await PromoteAsync(serviceState, request.ClientId).ConfigureAwait(false);
                    }
                    else
                        serviceState.IPC.BroadcastParticipants(serviceState.OwnerClientId, serviceState.PrimaryClientId);
                }
                finally
                {
                    serviceState.BrowserIPC.ControlChange.Release();
                }

                try
                {
                    await Task.Delay(Timeout.Infinite, linkedToken.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // Expected exception when cancellation occurs, don't treat as error
                    // Fall through to primary-disconnect handling.
                }
                catch (Exception ex)
                {
                    await OnConnectionClosed(serviceState, request.Id, request.ClientId, ex).ConfigureAwait(false);
                    return;
                }

                await OnConnectionClosed(serviceState, request.Id, request.ClientId, null).ConfigureAwait(false);
            }
            catch (RpcException ex) when (ex.StatusCode == StatusCode.PermissionDenied)
            {
                throw;
            }
            catch (Exception ex)
            {
                // serviceState may be null here if the wait above failed; only the current controller triggers teardown.
                if (serviceState?.EndReason is { } ending)
                    logger.LogDebug("Browser stream {ClientId} for session {Id} stopped: the session is already ending ({Reason}).", request.ClientId, request.Id, ending);
                else if (serviceState != null && serviceState.PrimaryClientId == request.ClientId)
                {
                    logger.LogError(ex, "Exception in ReceiveMessage outer catch for primary connection. Id: {Id}, ClientId: {ClientId}", request.Id, request.ClientId);
                    await shutdownService.Shutdown(request.Id, SessionEndReason.ControllerFailed, ex, $"controlling browser {request.ClientId}").ConfigureAwait(false);
                }
            }
            finally
            {
                if (attached && serviceState != null)
                    serviceState.ClientKeys.Detach(request.ClientId, keep: request.ClientId == serviceState.PrimaryClientId || request.ClientId == serviceState.OwnerClientId);
            }
        }

        private static string SignedInId(ServerCallContext context) =>
            CallContexts.Http(context)?.User is { Identity.IsAuthenticated: true } user ? user.GetUserObjectId() ?? string.Empty : string.Empty;

        /// <summary>
        /// A session limited to some users (SessionControllers) is claimed only by the owner's page or by one of them: any
        /// other member of the group may watch, but not take control first.
        /// </summary>
        private static bool MayClaimControl(ServiceState serviceState, ServerCallContext context)
        {
            if (!serviceState.Controllers.IsLimited)
                return true;
            var http = CallContexts.Http(context);
            return http is not null && (OwnerAccess.HasOwnerCookie(http, serviceState) || serviceState.Controllers.Lists(SignedInId(context)));
        }

        private static string SignedInName(ServerCallContext context)
        {
            try
            {
                return context.GetHttpContext().User?.GetUserDisplayName() ?? string.Empty;
            }
            catch (InvalidOperationException)
            {
                return string.Empty; // not an ASP.NET Core call (tests)
            }
        }

        /// <summary>
        /// A browser connection closed. If it was not the controlling connection, nothing happens. If it was, hand
        /// control back to the owner when the owner is still attached; otherwise tear the session down.
        /// </summary>
        private async Task OnConnectionClosed(ServiceState serviceState, string id, string clientId, Exception? ex)
        {
            // Decided under the control-change lock: a hand-off in progress finishes first, so this sees who really
            // holds control now (the leaver may just have been given it, or just have given it away).
            await serviceState.BrowserIPC.ControlChange.WaitAsync(CancellationToken.None).ConfigureAwait(false);
            try
            {
                if (serviceState.PrimaryClientId != clientId)
                {
                    // A mirror (or already-demoted connection) left; the session continues. Refresh the roster, dropping
                    // the leaver, which may not have been removed from the observer set yet.
                    serviceState.IPC.BroadcastParticipants(serviceState.OwnerClientId, serviceState.PrimaryClientId, excludeClientId: clientId);
                    return;
                }

                var owner = serviceState.OwnerClientId;
                if (!string.IsNullOrEmpty(owner) && owner != clientId && serviceState.IPC.HasConnection(owner))
                {
                    await PromoteAsync(serviceState, owner, excludeClientId: clientId).ConfigureAwait(false);
                    logger.LogInformation("Controlling connection {ClientId} left; control reverted to owner {Owner}. Id: {Id}", clientId, owner, id);
                    return;
                }
            }
            finally
            {
                serviceState.BrowserIPC.ControlChange.Release();
            }

            var ownerNote = string.IsNullOrEmpty(serviceState.OwnerClientId) ? "no owner" : $"owner {serviceState.OwnerClientId} not attached";
            await shutdownService.Shutdown(id, ex is null ? SessionEndReason.ControllerLeft : SessionEndReason.ControllerFailed, ex,
                $"controlling browser {clientId} left; {ownerNote}").ConfigureAwait(false);
        }

        /// <summary>
        /// Core of a control hand-off. Changing PrimaryClientId first immediately mutes the previous controller at the
        /// SendMessage gate; the sequence counter is then reset so the new controller (which resets its own outgoing
        /// sequence on the primaryChanged notice) lines up at 1; finally browsers are told who now has control.
        /// </summary>
        private static async Task PromoteAsync(ServiceState serviceState, string targetClientId, string? excludeClientId = null)
        {
            serviceState.PrimaryClientId = targetClientId;

            var state = serviceState.BrowserIPC;
            await state.Semaphore.WaitAsync(CancellationToken.None).ConfigureAwait(false);
            try
            {
                state.SequenceNum = 1;
                state.MessageDictionary.Clear();
            }
            finally
            {
                state.Semaphore.Release();
            }

            serviceState.IPC.SetPrimary(targetClientId);
            serviceState.IPC.BroadcastParticipants(serviceState.OwnerClientId, serviceState.PrimaryClientId, excludeClientId);
        }

        public override async Task<SendMessageResponse> SendMessage(SendSequenceMessageRequest request, ServerCallContext context)
        {
            // A new primary page starts at sequence 1 and may arrive before CreateWebView.
            // Later messages, including a dying WebView after deregister, must not create an entry.
            PendingSession? pending;
            if (request.IsPrimary && request.Sequence == 1)
            {
                if (authorization?.Enabled == true && !serviceDictionary.ContainsKey(request.Id))
                {
                    var http = CallContexts.Http(context);
                    if (http?.User?.Identity?.IsAuthenticated != true)
                    {
                        logger.LogWarning("Claim of control refused: not signed in to this session's group. Id: {Id}, ClientId: {ClientId}", request.Id, request.ClientId);
                        return new SendMessageResponse { Id = request.Id, Success = false };
                    }
                }
                pending = PendingSessions.Join(serviceDictionary, request.Id);
            }
            else if ((pending = PendingSessions.TryJoin(serviceDictionary, request.Id)) == null)
            {
                logger.LogDebug("Dropped browser SendMessage for client {Id} at sequence {Sequence}.", request.Id, request.Sequence);
                return new SendMessageResponse { Id = request.Id, Success = false };
            }
            using var _ = pending;
            var serviceStateTaskSource = pending.Source;

            try
            {
                // Must not be context.CancellationToken: a disconnecting browser would cancel this wait and shut down early.
                var serviceState = await serviceStateTaskSource.Task.WaitAsync(TimeSpan.FromSeconds(60), CancellationToken.None).ConfigureAwait(false);
                using CancellationTokenSource linkedToken = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken, serviceState.Token);

                // The first primary connection to send claims control for the session. Its stream may not be open yet, so
                // this can be the first the relay hears of the connection: its key is recorded here.
                // Only a connection the session's pages would let in may claim it. Nothing else needs the check here: every
                // later message is dropped unless it comes from the controller, and every controller was let in (by this
                // claim, by ReceiveMessage, or as a SetPrimary target, which must have an open stream).
                if (request.IsPrimary && string.IsNullOrEmpty(serviceState.PrimaryClientId))
                {
                    if (!await BrowserCallAccess.AllowsAsync(context, serviceState, authorization, users).ConfigureAwait(false))
                    {
                        logger.LogWarning("Claim of control refused: not signed in to this session's group. Id: {Id}, ClientId: {ClientId}", request.Id, request.ClientId);
                        return new SendMessageResponse { Id = request.Id, Success = false };
                    }
                    if (!MayClaimControl(serviceState, context))
                    {
                        logger.LogWarning("Claim of control refused: control is kept for the user who started this session. Id: {Id}, ClientId: {ClientId}", request.Id, request.ClientId);
                        return new SendMessageResponse { Id = request.Id, Success = false };
                    }
                    if (!serviceState.ClientKeys.Bind(request.ClientId, request.ClientKey))
                    {
                        logger.LogWarning("Claim of control refused: the clientId is in use with another key. Id: {Id}, ClientId: {ClientId}", request.Id, request.ClientId);
                        return new SendMessageResponse { Id = request.Id, Success = false };
                    }
                    serviceState.PrimaryClientId = request.ClientId;
                    serviceState.IPC.BroadcastParticipants(serviceState.OwnerClientId, serviceState.PrimaryClientId);
                }

                // Only the controlling connection's input reaches the WebView; every other connection is read-only.
                // Control moves at runtime via SetPrimary, so this is keyed on the live PrimaryClientId, not the
                // per-message isPrimary flag (which a client could also set on its own).
                if (request.ClientId != serviceState.PrimaryClientId)
                {
                    logger.LogDebug("Skipped send message '{Message}' from connection {ClientId}", request.Message, request.ClientId);
                    return new SendMessageResponse { Id = request.Id, Success = true };
                }

                // It names the controller: it must also be the controller. The controller's clientId is in every
                // browser's roster; its key is not.
                if (!serviceState.ClientKeys.Matches(request.ClientId, request.ClientKey))
                {
                    logger.LogWarning("Input refused: it names the controlling connection but does not carry its key. Id: {Id}, ClientId: {ClientId}", request.Id, request.ClientId);
                    return new SendMessageResponse { Id = request.Id, Success = false };
                }

                var state = serviceState.BrowserIPC;
                if (state == null)
                    return new SendMessageResponse { Id = request.Id, Success = false };

                await state.Semaphore.WaitAsync(linkedToken.Token).ConfigureAwait(false);
                try
                {
                    // Control may have moved while this message waited its turn. Its number belongs to the previous
                    // controller's numbering: forwarding it would inject stale input, and parking it would later take
                    // the place of the new controller's message with the same number.
                    if (request.ClientId != serviceState.PrimaryClientId)
                    {
                        logger.LogDebug("Skipped send message from connection {ClientId}: control moved while it waited", request.ClientId);
                        return new SendMessageResponse { Id = request.Id, Success = true };
                    }

                    if (request.Sequence == state.SequenceNum)
                    {
                        if (request.Message == "connected:")
                        {
                            request.Message += context.GetHttpContext().Connection.RemoteIpAddress + "|" + serviceState.User;
                            // Never forward relay/auth cookies (tickets, ASP.NET auth, owner/voice tokens) to the desktop client.
                            if (authorization?.Enabled == true)
                            {
                                var safeCookies = RelayCookieFilter.FilterForClient(serviceState.Cookies);
                                request.Cookies = safeCookies.Count > 0
                                    ? JsonSerializer.Serialize(safeCookies, JsonContext.Default.DictionaryStringString)
                                    : string.Empty;
                            }
                        }
                        await ForwardMessage(serviceState, request).ConfigureAwait(false);
                        state.SequenceNum++;
                    }
                    else
                    {
                        bool added = state.MessageDictionary.TryAdd(request.Sequence, request);
                        if (!added)
                        {
                            logger.LogError("Failed to add message to dictionary for Sequence: {Sequence}, Id: {Id}", request.Sequence, request.Id);
                        }
                    }

                    while (state.MessageDictionary.TryRemove(state.SequenceNum, out var queuedRequest))
                    {
                        await ForwardMessage(serviceState, queuedRequest).ConfigureAwait(false);
                        state.SequenceNum++;
                    }
                }
                finally
                {
                    state.Semaphore.Release();
                }

                return new SendMessageResponse { Id = request.Id, Success = true };
            }
            catch (Exception ex)
            {
                if (shutdownService.HasEnded(request.Id, out var ended))
                    logger.LogDebug("Browser SendMessage from {ClientId} for session {Id} dropped: the session ended ({Reason}).", request.ClientId, request.Id, ended);
                else
                    logger.LogError(ex, "Exception in SendMessage for Id: {Id}, ClientId: {ClientId}", request.Id, request.ClientId);
                return new SendMessageResponse { Id = request.Id, Success = false };
            }
        }
        /// <summary>
        /// Control transfer (hand-off). The current controller may pass control to any attached connection; the owner
        /// may always reclaim control to itself; nothing else is honored, so the remote user can never seize control.
        /// The requester is identified by its clientId and its key (ClientKeys), not by the clientId alone.
        /// </summary>
        public override async Task<Empty> SetPrimary(SetPrimaryRequest request, ServerCallContext context)
        {
            if (!serviceDictionary.TryGetValue(request.Id, out var tcs) || tcs == null || !tcs.Task.IsCompletedSuccessfully)
                return new Empty();

            var serviceState = tcs.Task.Result;
            if (!await BrowserCallAccess.AllowsAsync(context, serviceState, authorization, users, clientId: request.ClientId, clientKey: request.ClientKey).ConfigureAwait(false))
            {
                logger.LogWarning("SetPrimary refused: not signed in to this session's group. Id: {Id}, requester: {Requester}", request.Id, request.ClientId);
                return new Empty();
            }
            // One change of controller at a time: this request is judged against the result of any change before it.
            await serviceState.BrowserIPC.ControlChange.WaitAsync(context.CancellationToken).ConfigureAwait(false);
            try
            {
                return await SetPrimaryCore(serviceState, request).ConfigureAwait(false);
            }
            finally
            {
                serviceState.BrowserIPC.ControlChange.Release();
            }
        }

        private async Task<Empty> SetPrimaryCore(ServiceState serviceState, SetPrimaryRequest request)
        {
            string requester = request.ClientId;
            string target = request.Target;
            string current = serviceState.PrimaryClientId;
            string owner = serviceState.OwnerClientId;

            // The requester must be who it says: every browser knows the controller's and the owner's clientId (the
            // roster), only the connection itself knows its key. And it must be someone: before anyone has claimed
            // control, the controller's clientId is empty, which a request with no clientId would otherwise equal.
            bool allowed =
                !string.IsNullOrEmpty(requester) && serviceState.ClientKeys.Matches(requester, request.ClientKey) &&
                (requester == current ||                                                  // current controller passes control
                 (requester == owner && target == owner && !string.IsNullOrEmpty(owner))); // owner reclaims to itself

            if (!allowed)
            {
                logger.LogInformation("SetPrimary denied. Id: {Id}, requester: {Requester}, target: {Target}, current: {Current}, owner: {Owner}", request.Id, requester, target, current, owner);
                return new Empty();
            }

            if (string.IsNullOrEmpty(target) || !serviceState.IPC.HasConnection(target))
            {
                logger.LogInformation("SetPrimary target not attached. Id: {Id}, target: {Target}", request.Id, target);
                return new Empty();
            }

            if (target == current)
                return new Empty(); // already controlling

            // A session limited to some users (SessionControllers): control goes to one of them, so a page that hands control
            // to whoever joins (unattended sessions) cannot give it to another member who is only watching. A listed user who
            // holds control may still hand it to anyone on purpose, and that person is added to the list.
            var controllers = serviceState.Controllers;
            if (controllers.IsLimited && target != owner)
            {
                string targetUser = serviceState.IPC.ConnectionUserId(target) ?? string.Empty;
                bool handOff = requester == current && requester != owner && controllers.Lists(serviceState.IPC.ConnectionUserId(requester));
                if (!controllers.Lists(targetUser) && !handOff)
                {
                    logger.LogInformation("SetPrimary denied: control is kept for the user who started this session. Id: {Id}, requester: {Requester}, target: {Target}", request.Id, requester, target);
                    return new Empty();
                }
                controllers.Add(targetUser);
            }

            await PromoteAsync(serviceState, target).ConfigureAwait(false);
            logger.LogInformation("Control transferred. Id: {Id}, from: {From}, to: {To}", request.Id, current, target);
            return new Empty();
        }

        /// <summary>How long the app has to shut down after a restart request before the relay drops the session itself.</summary>
        internal static TimeSpan RestartGracePeriod { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>
        /// Restart the app from a browser (the session bar's Restart button). Allowed for the connection that has control
        /// and for the owner, when attached; ignored from anyone else. The app is sent the same "refreshed" notice a browser
        /// refresh sends, so an app that restarts on Refreshed needs nothing new; browsers are told first, so each page
        /// waits for the new copy. An app that does not shut down in time is dropped, as with a refresh.
        /// </summary>
        public override async Task<Empty> Restart(RestartRequest request, ServerCallContext context)
        {
            if (!serviceDictionary.TryGetValue(request.Id, out var tcs) || tcs == null || !tcs.Task.IsCompletedSuccessfully)
                return new Empty();

            var serviceState = tcs.Task.Result;
            string requester = request.ClientId;
            if (!await BrowserCallAccess.AllowsAsync(context, serviceState, authorization, users, clientId: requester, clientKey: request.ClientKey).ConfigureAwait(false))
            {
                logger.LogWarning("Restart refused: not signed in to this session's group. Id: {Id}, requester: {Requester}", request.Id, requester);
                return new Empty();
            }
            bool allowed = !string.IsNullOrEmpty(requester) && serviceState.IPC.HasConnection(requester) &&
                serviceState.ClientKeys.Matches(requester, request.ClientKey) && // the roster names both; only they hold their key
                (requester == serviceState.PrimaryClientId || requester == serviceState.OwnerClientId);

            if (!allowed)
            {
                logger.LogInformation("Restart denied. Id: {Id}, requester: {Requester}, current: {Current}, owner: {Owner}", request.Id, requester, serviceState.PrimaryClientId, serviceState.OwnerClientId);
                return new Empty();
            }

            if (Interlocked.Exchange(ref serviceState.RestartRequested, 1) == 1)
                return new Empty(); // already restarting

            logger.LogInformation("Restart requested. Id: {Id}, requester: {Requester}", request.Id, requester);
            serviceState.IPC.BroadcastRestarting();
            await serviceState.IPC.ReceiveMessage(new WebMessageResponse { Response = "refreshed:" }).ConfigureAwait(false);

            // Not awaited: the browser's request returns now; the session is dropped later only if the app never shuts down.
            _ = Task.Run(async () =>
            {
                try
                {
                    if (!await Endpoints.WaitForClientShutdown(serviceDictionary, request.Id, RestartGracePeriod, TimeSpan.FromMilliseconds(100)).ConfigureAwait(false))
                    {
                        logger.LogWarning("App did not shut down after a restart request; dropping the session. Id: {Id}", request.Id);
                        await shutdownService.Shutdown(request.Id, SessionEndReason.RestartNotHonoured, detail: $"requested by {requester}").ConfigureAwait(false);
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Restart follow-up failed. Id: {Id}", request.Id);
                }
            }, CancellationToken.None);

            return new Empty();
        }

        private static ValueTask ForwardMessage(ServiceState serviceState, SendSequenceMessageRequest request)
        {
            // Browser-only state must follow the same sequence ordering as Blazor events,
            // but must never reach the desktop Blazor IPC parser.
            return request.Message.StartsWith(IPC.ScrollMessagePrefix, StringComparison.Ordinal)
                ? serviceState.IPC.SendMessage(request.Message)
                : serviceState.IPC.ReceiveMessage(new WebMessageResponse
                {
                    Response = request.Message, Url = request.Url, Cookies = request.Cookies
                });
        }
    }
}
