using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.Extensions.Logging;
#if AUTHORIZATION
using Newtonsoft.Json;
using System.Linq;
#endif
using PeakSWC.RemoteWebView.Services;
using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace PeakSWC.RemoteWebView
{
    public class BrowserIPCService(ILogger<RemoteWebViewService> logger, ConcurrentDictionary<string, TaskCompletionSource<ServiceState>> serviceDictionary, ShutdownService shutdownService) : BrowserIPC.BrowserIPCBase
    {
        public override async Task ReceiveMessage(ReceiveMessageRequest request, IServerStreamWriter<StringRequest> responseStream, ServerCallContext context)
        {
            var serviceStateTaskSource = serviceDictionary.GetOrAdd(request.Id, _ => new TaskCompletionSource<ServiceState>(TaskCreationOptions.RunContinuationsAsynchronously));

            ServiceState? serviceState = null;
            try
            {
                // Must not be context.CancellationToken: a disconnecting browser would cancel this wait and shut down early.
                serviceState = await serviceStateTaskSource.Task.WaitAsync(TimeSpan.FromSeconds(60), CancellationToken.None).ConfigureAwait(false);

                // The machine owner's local control surface identifies itself (?role=owner). Only the owner may reclaim control.
                if (request.IsOwner)
                    serviceState.OwnerClientId = request.ClientId;

                using var linkedToken = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken, serviceState.Token);

                serviceState.IPC.BrowserResponseStream(new BrowserResponseNode(responseStream, request.ClientId, request.IsPrimary), linkedToken);

                // The machine owner holds control from the start. The owner's surface may connect after the technician
                // (in mirror mode it connects only once the technician is present), so promote it on its first
                // connection even if the technician already claimed control - but only once, so a later owner reconnect
                // does not grab control back. PromoteAsync also broadcasts the roster; otherwise just tell everyone present.
                if (request.IsOwner && !serviceState.OwnerControlInitialized)
                {
                    serviceState.OwnerControlInitialized = true;
                    await PromoteAsync(serviceState, request.ClientId).ConfigureAwait(false);
                }
                else
                    serviceState.IPC.BroadcastParticipants(serviceState.OwnerClientId, serviceState.PrimaryClientId);

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
            catch (Exception ex)
            {
                // serviceState may be null here if the wait above failed; only the current controller triggers teardown.
                if (serviceState != null && serviceState.PrimaryClientId == request.ClientId)
                {
                    logger.LogError(ex, "Exception in ReceiveMessage outer catch for primary connection. Id: {Id}, ClientId: {ClientId}", request.Id, request.ClientId);
                    await shutdownService.Shutdown(request.Id, ex).ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// A browser connection closed. If it was not the controlling connection, nothing happens. If it was, hand
        /// control back to the owner when the owner is still attached; otherwise tear the session down.
        /// </summary>
        private async Task OnConnectionClosed(ServiceState serviceState, string id, string clientId, Exception? ex)
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

            logger.LogInformation("Controlling connection closed, shutting down. Id: {Id}, ClientId: {ClientId}", id, clientId);
            if (ex != null)
                await shutdownService.Shutdown(id, ex).ConfigureAwait(false);
            else
                await shutdownService.Shutdown(id).ConfigureAwait(false);
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
            await state.Semaphore.WaitAsync().ConfigureAwait(false);
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
            TaskCompletionSource<ServiceState> serviceStateTaskSource;
            if (request.IsPrimary && request.Sequence == 1)
            {
                serviceStateTaskSource = serviceDictionary.GetOrAdd(request.Id, _ => new TaskCompletionSource<ServiceState>(TaskCreationOptions.RunContinuationsAsynchronously));
            }
            else if (!serviceDictionary.TryGetValue(request.Id, out var existing) || existing == null)
            {
                logger.LogDebug("Dropped browser SendMessage for client {Id} at sequence {Sequence}.", request.Id, request.Sequence);
                return new SendMessageResponse { Id = request.Id, Success = false };
            }
            else
            {
                serviceStateTaskSource = existing;
            }

            try
            {
                // Must not be context.CancellationToken: a disconnecting browser would cancel this wait and shut down early.
                var serviceState = await serviceStateTaskSource.Task.WaitAsync(TimeSpan.FromSeconds(60), CancellationToken.None).ConfigureAwait(false);
                using CancellationTokenSource linkedToken = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken, serviceState.Token);

                // The first primary connection to send claims control for the session.
                if (request.IsPrimary && string.IsNullOrEmpty(serviceState.PrimaryClientId))
                {
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

                var state = serviceState.BrowserIPC;
                if (state == null)
                    return new SendMessageResponse { Id = request.Id, Success = false };

                await state.Semaphore.WaitAsync(linkedToken.Token).ConfigureAwait(false);
                try
                {
                    if (request.Sequence == state.SequenceNum)
                    {
                        if (request.Message == "connected:")
                        {
                            request.Message += context.GetHttpContext().Connection.RemoteIpAddress + "|" + serviceState.User;
#if AUTHORIZATION
                            string serializedCookies = string.Empty;
                            if (serviceState.Cookies != null)
                                serializedCookies = JsonConvert.SerializeObject(serviceState.Cookies.ToDictionary(c => c.Key, c => c.Value));
                            request.Cookies = serializedCookies;
#endif
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
                logger.LogError(ex, "Exception in SendMessage for Id: {Id}, ClientId: {ClientId}", request.Id, request.ClientId);
                return new SendMessageResponse { Id = request.Id, Success = false };
            }
        }
        /// <summary>
        /// Control transfer (hand-off). The current controller may pass control to any attached connection; the owner
        /// may always reclaim control to itself; nothing else is honored, so the tech can never seize control.
        /// </summary>
        public override async Task<Empty> SetPrimary(SetPrimaryRequest request, ServerCallContext context)
        {
            if (!serviceDictionary.TryGetValue(request.Id, out var tcs) || tcs == null || !tcs.Task.IsCompletedSuccessfully)
                return new Empty();

            var serviceState = tcs.Task.Result;
            string requester = request.ClientId;
            string target = request.Target;
            string current = serviceState.PrimaryClientId;
            string owner = serviceState.OwnerClientId;

            bool allowed =
                requester == current ||                                               // current controller passes control
                (requester == owner && target == owner && !string.IsNullOrEmpty(owner)); // owner reclaims to itself

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

            await PromoteAsync(serviceState, target).ConfigureAwait(false);
            logger.LogInformation("Control transferred. Id: {Id}, from: {From}, to: {To}", request.Id, current, target);
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
