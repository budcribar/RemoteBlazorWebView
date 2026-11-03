using Grpc.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using PeakSWC.RemoteWebView.Agent;
using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace PeakSWC.RemoteWebView.Agents
{
    /// <summary>AgentHub: the long-lived stream each Remote PC Diagnostics agent keeps open (native gRPC, anonymous; enrollment key checked here).</summary>
    public class AgentHubService(ILogger<AgentHubService> logger, AgentRegistry registry, AgentOptions options) : AgentHub.AgentHubBase
    {
        private const string Category = "PeakSWC.RemoteWebView.Agents.AgentHubService";
        private static int warnedAnyKey;

        public override async Task Connect(IAsyncStreamReader<AgentMessage> requestStream, IServerStreamWriter<HubCommand> responseStream, ServerCallContext context)
        {
            var callToken = context.CancellationToken;

            if (!await requestStream.MoveNext(callToken).ConfigureAwait(false) || requestStream.Current.BodyCase != AgentMessage.BodyOneofCase.Hello)
                throw new RpcException(new Status(StatusCode.InvalidArgument, "The first message must be AgentHello"));

            var hello = requestStream.Current.Hello;
            if (string.IsNullOrWhiteSpace(hello.MachineId))
                throw new RpcException(new Status(StatusCode.InvalidArgument, "AgentHello.machine_id is required"));

            string ip = GetRemoteIp(context);

            if (options.AcceptsAnyKey)
            {
                if (Interlocked.Exchange(ref warnedAnyKey, 1) == 0)
                    AgentEvents.Warn(logger, $"{AgentOptions.EnrollmentKeysVariable} is not set: any agent enrollment key is accepted");
            }
            else if (!options.IsKeyAccepted(hello.EnrollmentKey))
            {
                AgentEvents.Warn(logger, $"Agent {hello.MachineName} ({hello.MachineId}) from {ip} was refused: enrollment key not accepted");
                throw new RpcException(new Status(StatusCode.PermissionDenied, "The enrollment key was not accepted"));
            }

            var connection = registry.Register(hello, ip, responseStream);
            AgentEvents.Info(logger, Category,
                $"Agent {(connection.IsNew ? "registered" : connection.ReplacedExisting ? "reconnected (replaced its previous stream)" : "reconnected")}: " +
                $"{hello.MachineName} ({hello.MachineId}) from {ip}, user '{hello.SignedInUser}', agent {hello.AgentVersion}, app '{hello.CachedAppVersion}', " +
                $"consent {hello.ConsentCeiling}, lifetime {hello.Lifetime}");

            using var streamCts = CancellationTokenSource.CreateLinkedTokenSource(callToken, connection.Replaced);
            var token = streamCts.Token;
            var pump = PumpCommandsAsync(connection, responseStream, streamCts);

            try
            {
                while (await requestStream.MoveNext(token).ConfigureAwait(false))
                {
                    var message = requestStream.Current;
                    switch (message.BodyCase)
                    {
                        case AgentMessage.BodyOneofCase.Heartbeat:
                            registry.OnHeartbeat(connection, message.Heartbeat);
                            break;
                        case AgentMessage.BodyOneofCase.Progress:
                            var stored = registry.OnProgress(connection, message.Progress);
                            if (stored != null)
                                LogProgress(hello, stored);
                            break;
                        case AgentMessage.BodyOneofCase.Hello:
                            logger.LogWarning("Agent {MachineId} sent a second AgentHello; ignored", hello.MachineId);
                            break;
                    }
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                // Call ended or another stream registered for this machine.
            }
            catch (Exception ex) when (ex is System.IO.IOException or RpcException)
            {
                logger.LogDebug(ex, "Agent {MachineId} stream ended", hello.MachineId);
            }
            finally
            {
                streamCts.Cancel();
                try { await pump.ConfigureAwait(false); } catch { }

                if (registry.Disconnect(connection))
                    AgentEvents.Info(logger, Category, $"Agent disconnected: {hello.MachineName} ({hello.MachineId})");
            }
        }

        private async Task PumpCommandsAsync(AgentConnection connection, IServerStreamWriter<HubCommand> responseStream, CancellationTokenSource streamCts)
        {
            try
            {
                await foreach (var command in connection.Commands.ReadAllAsync(streamCts.Token).ConfigureAwait(false))
                    await responseStream.WriteAsync(command).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (streamCts.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Sending a command to agent {MachineId} failed; closing its stream", connection.MachineId);
                streamCts.Cancel();
            }
        }

        private void LogProgress(AgentHello hello, LaunchProgress progress)
        {
            string where = $"{hello.MachineName} ({hello.MachineId}), request {progress.RequestId}, session {progress.SessionId}";
            switch (progress.Stage)
            {
                case LaunchStage.StageReady:
                    AgentEvents.Info(logger, Category, $"Diagnostics ready on {where}");
                    break;
                case LaunchStage.StageDeclined:
                    AgentEvents.Info(logger, Category, $"Diagnostics declined on {where}: {progress.Detail}");
                    break;
                case LaunchStage.StageFailed:
                    AgentEvents.Warn(logger, $"Diagnostics failed to start on {where}: {progress.Detail}");
                    break;
                case LaunchStage.StageEnded:
                    AgentEvents.Info(logger, Category, $"Diagnostics ended on {where}");
                    break;
                default:
                    logger.LogDebug("Launch progress {Stage} on {Where}: {Detail}", progress.Stage, where, progress.Detail);
                    break;
            }
        }

        public static string GetRemoteIp(ServerCallContext context)
        {
            IPAddress? address = null;
            try
            {
                address = context.GetHttpContext().Connection.RemoteIpAddress;
            }
            catch (InvalidOperationException)
            {
                // Not hosted by ASP.NET Core (unit tests).
            }

            if (address == null)
                return context.Peer ?? string.Empty;
            if (address.IsIPv4MappedToIPv6)
                address = address.MapToIPv4();
            return address.ToString();
        }
    }
}
