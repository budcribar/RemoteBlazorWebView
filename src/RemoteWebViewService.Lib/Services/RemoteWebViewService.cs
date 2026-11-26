using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using PeakSWC.RemoteWebView.Agents;
using PeakSWC.RemoteWebView.Services;
using PeakSWC.RemoteWebView.RemoteStaticFiles;
using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace PeakSWC.RemoteWebView
{
    public class RemoteWebViewService(ILogger<RemoteWebViewService> logger, ConcurrentDictionary<string, TaskCompletionSource<ServiceState>> serviceDictionary, ConcurrentDictionary<string, Channel<string>> serviceStateChannel, ShutdownService shutdownService, ServerFileSyncManager _fileSyncManager, IShopSessionPolicy? shopSessions = null, IRelayAuthorization? authorization = null, AgentRegistry? agents = null) : WebViewIPC.WebViewIPCBase
    {
        private const string CreateFailed = "createFailed:";
        /// <summary>
        /// Every session id on the relay. Refused when the relay requires sign-in: a session id is what the browser calls
        /// use to attach, so listing them to an anonymous caller would hand out every shop's sessions.
        /// </summary>
        public override Task<IdArrayResponse> GetIds(Empty request, ServerCallContext context)
        {
            if (authorization?.Enabled == true)
                throw new RpcException(new Status(StatusCode.PermissionDenied, "Listing session ids is turned off on a relay that requires sign-in."));

            var results = new IdArrayResponse();
            results.Responses.AddRange(serviceDictionary.Keys);
            return Task.FromResult(results);
        }

        public override async Task CreateWebView(CreateWebViewRequest request, IServerStreamWriter<WebMessageResponse> responseStream, ServerCallContext context)
        {         
            if (request == null || string.IsNullOrWhiteSpace(request.Id))
            {
                logger.LogError("CreateWebView called with null or empty Id.");
                await responseStream.WriteAsync(new WebMessageResponse { Response = CreateFailed }, CancellationToken.None).ConfigureAwait(false);
                return;
            }

            logger.LogDebug("CreateWebView called for Id: {Id}", request.Id);

            // A completed registration is a duplicate id. Refuse it before the shop cap so a retry of the same
            // session is not counted as a second seat.
            if (serviceDictionary.TryGetValue(request.Id, out var existing) && existing.Task.IsCompleted)
            {
                logger.LogError("CreateWebView failed to add client for Id: {Id}", request.Id);
                await responseStream.WriteAsync(new WebMessageResponse { Response = CreateFailed }, CancellationToken.None).ConfigureAwait(false);
                return;
            }

            var group = request.Group ?? string.Empty;
            var active = ShopSessionActivity.Count(serviceDictionary, group);
            var decision = await (shopSessions ?? AllowAllShopSessionPolicy.Instance).CheckCreateSessionAsync(group, active, context.CancellationToken).ConfigureAwait(false);
            if (!decision.Allowed)
            {
                logger.LogWarning("CreateWebView denied for Id {Id} in group {Group}: {ErrorCode}. {Message}", request.Id, group, decision.ErrorCode, decision.Message);
                await responseStream.WriteAsync(new WebMessageResponse { Response = CreateFailed + (decision.ErrorCode ?? "denied") }, CancellationToken.None).ConfigureAwait(false);
                return;
            }

            using var pending = PendingSessions.Join(serviceDictionary, request.Id);
            var serviceStateTaskSource = pending.Source;

            if (serviceStateTaskSource.Task.IsCompleted)
            {
                logger.LogError("CreateWebView failed to add client for Id: {Id}", request.Id);
                await responseStream.WriteAsync(new WebMessageResponse { Response = CreateFailed }, CancellationToken.None).ConfigureAwait(false);
                return;
            }     

            ServiceState state = new(logger, request.EnableMirrors)
            {
                Voice = VoiceRoom.Create(request.VoiceHostKey, context.CancellationToken),
                HtmlHostPath = request.HtmlHostPath,
                Markup = request.Markup,
                InUse = false,
                Url = $"https://{context.Host}/app/{request.Id}",
                Id = request.Id,
                Group = group,
                Pid = request.Pid,
                HostName = request.HostName,
                ProcessName = request.ProcessName,
                OwnerKey = request.OwnerKey,
                AppKey = request.AppKey,
            };
            state.IPC.ParticipantEvents = request.ParticipantEvents;
            // A session an agent started for a signed-in user: only that user is given control (others may watch).
            state.Controllers.Add(agents?.StartedBy(request.Id));

            using CancellationTokenSource linkedToken = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken, state.Token);
            try
            {
                // No need to wait for writes to complete
                foreach (var x in serviceStateChannel.Values)
                    x.Writer.TryWrite($"Start:{request.Id}");
                state.IPC.ClientResponseStream = responseStream;
                // Once the app's call has ended its response stream is complete, and a write to it throws; drop it so a shutdown
                // notice for an app that has already gone is skipped instead of failing.
                using var dropStream = context.CancellationToken.Register(() => state.IPC.ClientResponseStream = null);

                await responseStream.WriteAsync(new WebMessageResponse { Response = "created:" }, CancellationToken.None).ConfigureAwait(false);

                shutdownService.Forget(request.Id);
                serviceStateTaskSource.SetResult(state);
                // Pairs with the shutdown line (ShutdownService), which says when and why the id was given up.
                logger.LogInformation("Session {Id} registered: {ProcessName} (pid {Pid}) on {HostName}, group '{Group}', owner key: {Keyed}, app key: {AppKeyed}, mirrors: {Mirrors}",
                    request.Id, request.ProcessName, request.Pid, request.HostName, group, OwnerAccess.IsKeyed(state) ? "yes" : "no", AppAccess.IsKeyed(state) ? "yes" : "no", request.EnableMirrors ? "on" : "off");

                await Task.Delay(Timeout.Infinite, linkedToken.Token).ConfigureAwait(false);
            }
            catch (TaskCanceledException) when (serviceStateTaskSource.Task.IsCompleted)
            {
                // Expected when cancellation is requested; no action needed.
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "CreateWebView failed for Id: {Id}", request.Id);
                if (!serviceStateTaskSource.Task.IsCompleted)
                {
                    serviceDictionary.TryRemove(System.Collections.Generic.KeyValuePair.Create(request.Id, serviceStateTaskSource));
                    serviceStateTaskSource.TrySetCanceled(CancellationToken.None);
                    await state.DisposeAsync().ConfigureAwait(false);
                    return;
                }
                await shutdownService.Shutdown(request.Id, SessionEndReason.CreateWebViewFailed, ex).ConfigureAwait(false);
            }         
        }
     
        public override async Task RequestClientFileRead(IAsyncStreamReader<ClientFileReadResponse> requestStream, IServerStreamWriter<ServerFileReadRequest> responseStream, ServerCallContext context)
        {
            // Handle Init message
            if (!await requestStream.MoveNext(context.CancellationToken).ConfigureAwait(false))
            {
                logger.LogWarning("Client disconnected without sending Init message.");
                throw new RpcException(new Status(StatusCode.InvalidArgument, "Init message not received."));
            }

            var initResponse = requestStream.Current;

            if (initResponse?.Init == null)
            {
                logger.LogWarning("First message from client is not Init.");
                throw new RpcException(new Status(StatusCode.InvalidArgument, "First message must be Init."));
            }

            string clientGuid = initResponse.ClientId;
            if (string.IsNullOrWhiteSpace(clientGuid))
            {
                logger.LogWarning("Init message from client has empty ClientId.");
                throw new RpcException(new Status(StatusCode.InvalidArgument, "ClientId cannot be empty."));
            }

            logger.LogDebug("Client '{ClientGuid}' connected and initialized.", clientGuid);

            using var pending = PendingSessions.Join(serviceDictionary, clientGuid);
            var serviceStateTaskSource = pending.Source;
            ServiceState serviceState;
            try
            {
                serviceState = await serviceStateTaskSource.Task.WaitAsync(TimeSpan.FromSeconds(60), CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error handling client '{ClientGuid}' responses.", clientGuid);
                await shutdownService.Shutdown(clientGuid, SessionEndReason.FileReadStreamEnded).ConfigureAwait(false);
                return;
            }

            // Checked before the stream is registered: whoever registers it serves the session's files to its browsers.
            if (!AppAccess.Allows(serviceState, context))
            {
                logger.LogWarning("RequestClientFileRead for session {ClientGuid} refused: missing or wrong {Header}.", clientGuid, AppAccess.HeaderName);
                throw new RpcException(new Status(StatusCode.PermissionDenied, "Not this session's app."));
            }

            // Register the client
            _fileSyncManager.RegisterClient(clientGuid, initResponse.Init.HtmlHostPath);

            // Associate the response stream with the clientGuid
            _fileSyncManager.AssociateResponseStream(clientGuid, responseStream);

            try
            {
                serviceState.FileManagerReady.TrySetResult(true);

                // Create a linked cancellation token to handle both client cancellation and server cancellation
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken, serviceState.Token);
                var cancellationToken = linkedCts.Token;

                // Continuously read messages from the client until cancellation
                while (await requestStream.MoveNext(cancellationToken).ConfigureAwait(false))
                {
                    var response = requestStream.Current;
                    await _fileSyncManager.HandleClientResponse(response).ConfigureAwait(false);
                }

                logger.LogDebug("Client '{ClientGuid}' has completed sending messages.", clientGuid);

            }
            catch (OperationCanceledException) { }
            catch (Exception ex) when (shutdownService.HasEnded(clientGuid, out var ended))
            {
                // The session ended first (e.g. an admin ended it and the agent then closed the app): the abort is expected.
                logger.LogDebug(ex, "File-read stream for session {ClientGuid} stopped after the session ended ({Reason}).", clientGuid, ended);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error handling client '{ClientGuid}' responses.", clientGuid);
            }
            finally
            {
                // Clean up when the client disconnects
                _fileSyncManager.RemoveClient(clientGuid);
                logger.LogDebug("Cleaned up resources for client '{ClientGuid}'.", clientGuid);
                await shutdownService.Shutdown(clientGuid, SessionEndReason.FileReadStreamEnded).ConfigureAwait(false);
            }
        }
        public override async Task<Empty> Shutdown(IdMessageRequest request, ServerCallContext context)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Id))
                return new Empty();

            // Only the session's own app may end it (a registration still pending has no key to check yet).
            if (serviceDictionary.TryGetValue(request.Id, out var source) && source.Task.IsCompletedSuccessfully && !AppAccess.Allows(source.Task.Result, context))
            {
                logger.LogWarning("Shutdown of session {Id} refused: missing or wrong {Header}.", request.Id, AppAccess.HeaderName);
                throw new RpcException(new Status(StatusCode.PermissionDenied, "Not this session's app."));
            }

            await shutdownService.Shutdown(request.Id, SessionEndReason.RequestedByApp).ConfigureAwait(false);
            return new Empty();
        }

        public override async Task<SendMessageResponse> SendMessage(SendMessageRequest request, ServerCallContext context)
        {
            using var pending = PendingSessions.Join(serviceDictionary, request.Id);
            var serviceStateTaskSource = pending.Source;

            try
            {
                var serviceState = await serviceStateTaskSource.Task.WaitAsync(TimeSpan.FromSeconds(60), CancellationToken.None).ConfigureAwait(false);
                if (!AppAccess.Allows(serviceState, context))
                {
                    logger.LogWarning("SendMessage to session {Id} refused: missing or wrong {Header}.", request.Id, AppAccess.HeaderName);
                    return new SendMessageResponse { Id = request.Id, Success = false };
                }
                await serviceState.IPC.SendMessage(request.Message).ConfigureAwait(false);
                return new SendMessageResponse { Id = request.Id, Success = true };
            }
            catch (Exception ex)
            {
                if (shutdownService.HasEnded(request.Id, out var ended))
                    logger.LogDebug("SendMessage from the app for session {Id} dropped: the session ended ({Reason}).", request.Id, ended);
                else
                    logger.LogError(ex, "SendMessage failed for Id: {Id}", request.Id);
            }

            return new SendMessageResponse { Id = request.Id, Success = false };
        }

        public override async Task Ping(IAsyncStreamReader<PingMessageRequest> requestStream, IServerStreamWriter<PingMessageResponse> responseStream, ServerCallContext context)
        {
            var id = string.Empty;
            bool refused = false;
            // One source for the read loop and the writer, created once the client registration exists.
            CancellationTokenSource? linkedToken = null;
            CancellationTokenSource activeToken;
            try
            {
                DateTime responseReceived = DateTime.UtcNow;
                DateTime responseSent = DateTime.UtcNow;

                ServiceState? serviceState = null;

                if (!await requestStream.MoveNext(context.CancellationToken).ConfigureAwait(false))
                    return;

                do
                {
                    var message = requestStream.Current;
                    if (message == null || string.IsNullOrWhiteSpace(message.Id))
                        return;

                    id = message.Id;
                    if (serviceState == null)
                    {
                        using (var pending = PendingSessions.Join(serviceDictionary, id.ToString()))
                            serviceState = await pending.Source.Task.WaitAsync(TimeSpan.FromSeconds(60), CancellationToken.None).ConfigureAwait(false);
                        if (!AppAccess.Allows(serviceState, context))
                        {
                            // Not the app: the session is left alone (shutting it down here would hand anyone a way to end it).
                            logger.LogWarning("Ping for session {Id} refused: missing or wrong {Header}.", id, AppAccess.HeaderName);
                            id = string.Empty;
                            refused = true;
                            throw new RpcException(new Status(StatusCode.PermissionDenied, "Not this session's app."));
                        }
                        linkedToken = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken, serviceState.Token);
                    }
                    activeToken = linkedToken ?? throw new InvalidOperationException("Ping registration was not created.");
                    if (message.Initialize && serviceState.PingTask == null)
                    {
                        var writerToken = activeToken;
                        serviceState.PingTask = Task.Run(async () =>
                        {
                            try
                            {
                                while (!writerToken.Token.IsCancellationRequested)
                                {
                                    responseSent = DateTime.UtcNow;
                                    await responseStream.WriteAsync(new PingMessageResponse { Id = id, Cancelled = false }, writerToken.Token).ConfigureAwait(false);
                                    await Task.Delay(TimeSpan.FromSeconds(message.PingIntervalSeconds > 0 ? message.PingIntervalSeconds : 30), writerToken.Token).ConfigureAwait(false);
                                    if (responseReceived < responseSent)
                                    {
                                        await responseStream.WriteAsync(new PingMessageResponse { Id = id, Cancelled = true }, writerToken.Token).ConfigureAwait(false);
                                        logger.LogWarning("Ping timeout for Id: {Id}", id);
                                        await shutdownService.Shutdown(id, SessionEndReason.PingTimeout, detail: $"no reply within {(message.PingIntervalSeconds > 0 ? message.PingIntervalSeconds : 30)} s").ConfigureAwait(false);
                                        break;
                                    }
                                }
                            }
                            catch (OperationCanceledException)
                            {
                            }
                            catch (ObjectDisposedException)
                            {
                                // The linked source is disposed when Ping returns.
                            }
                        }, writerToken.Token);
                    }
                    else
                    {
                        responseReceived = DateTime.UtcNow;
                        var delta = responseReceived.Subtract(responseSent);
                        if (delta > serviceState.MaxClientPing)
                            serviceState.MaxClientPing = delta;
                    }
                }
                while (await requestStream.MoveNext(activeToken.Token).ConfigureAwait(false));
            }
            catch (RpcException) when (refused)
            {
                throw;
            }
            catch (OperationCanceledException ex)
            {
                if (shutdownService.HasEnded(id, out var ended))
                    logger.LogDebug(ex, "Ping stream for session {Id} stopped: the session is already ending ({Reason}).", id, ended);
                else if (!string.IsNullOrWhiteSpace(id))
                    // The session was still running, so the app's own ping stream went away: its connection closed or dropped.
                    await shutdownService.Shutdown(id, SessionEndReason.PingStreamClosed).ConfigureAwait(false);
            }
            catch (Exception ex) when (shutdownService.HasEnded(id, out var ended))
            {
                logger.LogDebug(ex, "Ping stream for session {Id} stopped after the session ended ({Reason}).", id, ended);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Exception in Ping for Id: {Id}", id);
                if (!string.IsNullOrWhiteSpace(id))
                    await shutdownService.Shutdown(id, SessionEndReason.PingStreamFailed, ex).ConfigureAwait(false);
            }
            finally
            {
                linkedToken?.Dispose();
            }
        }
    }
}
