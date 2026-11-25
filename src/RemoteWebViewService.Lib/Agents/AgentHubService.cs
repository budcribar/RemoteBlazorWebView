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
    /// <summary>AgentHub: the long-lived stream each agent keeps open (native gRPC, anonymous; enrollment key checked here).</summary>
    [System.Diagnostics.CodeAnalysis.Experimental(ExperimentalApi.AgentRegistry, UrlFormat = ExperimentalApi.UrlFormat)]
    public class AgentHubService(ILogger<AgentHubService> logger, AgentRegistry registry, AgentOptions options, PeakSWC.RemoteWebView.IAgentTenantPolicy? tenantPolicy = null) : AgentHub.AgentHubBase
    {
        private const string Category = "PeakSWC.RemoteWebView.Agents.AgentHubService";
        private readonly PeakSWC.RemoteWebView.IAgentTenantPolicy policy = tenantPolicy ?? new PeakSWC.RemoteWebView.AllowAllAgentTenantPolicy();
        private static int warnedAnyKey;
        private static int warnedNoKeys;

        public override async Task Connect(IAsyncStreamReader<AgentMessage> requestStream, IServerStreamWriter<HubCommand> responseStream, ServerCallContext context)
        {
            var callToken = context.CancellationToken;
            var hello = await ReadHelloAsync(requestStream, callToken).ConfigureAwait(false);
            string ip = GetRemoteIp(context);
            var decision = await AcceptEnrollmentAsync(hello, ip, callToken).ConfigureAwait(false);

            AgentConnection connection;
            try
            {
                connection = registry.Register(hello, ip, responseStream, decision.Group, decision.IssuedAgentToken);
            }
            catch (InvalidOperationException ex)
            {
                throw new RpcException(new Status(StatusCode.PermissionDenied, ex.Message));
            }
            AgentEvents.Info(logger, Category, DescribeRegistration(connection, hello, ip));

            using var streamCts = CancellationTokenSource.CreateLinkedTokenSource(callToken, connection.Replaced);
            var token = streamCts.Token;
            var pump = PumpCommandsAsync(connection, responseStream, streamCts);

            try
            {
                while (await requestStream.MoveNext(token).ConfigureAwait(false))
                    HandleMessage(hello, connection, requestStream.Current);
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
                await streamCts.CancelAsync().ConfigureAwait(false);
                try
                {
                    await pump.ConfigureAwait(false);
                }
                catch (Exception)
                {
                    // PumpCommandsAsync already logged a send failure. Disconnect must still run.
                }

                if (registry.Disconnect(connection, out var failedLaunches))
                {
                    AgentEvents.Info(logger, Category, $"Agent disconnected: {hello.MachineName} ({hello.MachineId})");
                    foreach (var failed in failedLaunches)
                        LogProgress(hello, failed);
                }
            }
        }

        private static async Task<AgentHello> ReadHelloAsync(IAsyncStreamReader<AgentMessage> requestStream, CancellationToken callToken)
        {
            if (!await requestStream.MoveNext(callToken).ConfigureAwait(false) || requestStream.Current.BodyCase != AgentMessage.BodyOneofCase.Hello)
                throw new RpcException(new Status(StatusCode.InvalidArgument, "The first message must be AgentHello"));

            var hello = requestStream.Current.Hello;
            if (string.IsNullOrWhiteSpace(hello.MachineId))
                throw new RpcException(new Status(StatusCode.InvalidArgument, "AgentHello.machine_id is required"));
            return hello;
        }

        private async Task<PeakSWC.RemoteWebView.AgentEnrollmentDecision> AcceptEnrollmentAsync(AgentHello hello, string ip, CancellationToken cancellationToken)
        {
            var decision = await policy.CheckEnrollmentAsync(hello.EnrollmentKey, hello.MachineId, hello.AgentToken, options, cancellationToken).ConfigureAwait(false);
            bool keysDecide = policy is PeakSWC.RemoteWebView.AllowAllAgentTenantPolicy;
            if (!decision.Accepted)
            {
                if (keysDecide && options.RefusesEveryKey && Interlocked.Exchange(ref warnedNoKeys, 1) == 0)
                    AgentEvents.Warn(logger, $"{AgentOptions.EnrollmentKeysVariable} is not set: every agent is refused. Set it to the keys your agents were installed with.");
                AgentEvents.Warn(logger, $"Agent {hello.MachineName} ({hello.MachineId}) from {ip} was refused: enrollment key not accepted ({decision.DenialReason ?? "no reason given"})");
                throw new RpcException(new Status(StatusCode.PermissionDenied, decision.DenialReason ?? "The enrollment key was not accepted"));
            }

            if (keysDecide && options.AcceptsAnyKey && Interlocked.Exchange(ref warnedAnyKey, 1) == 0)
                AgentEvents.Warn(logger, $"{AgentOptions.EnrollmentKeysVariable} is not set: any agent enrollment key is accepted");
            return decision;
        }

        private static string DescribeRegistration(AgentConnection connection, AgentHello hello, string ip)
        {
            string how;
            if (connection.IsNew)
                how = "registered";
            else if (connection.ReplacedExisting)
                how = "reconnected (replaced its previous stream)";
            else
                how = "reconnected";

            return $"Agent {how}: {hello.MachineName} ({hello.MachineId}) from {ip}, user '{hello.SignedInUser}', agent {hello.AgentVersion}, app '{hello.CachedAppVersion}', consent {hello.ConsentCeiling}, lifetime {hello.Lifetime}";
        }

        private void HandleMessage(AgentHello hello, AgentConnection connection, AgentMessage message)
        {
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
                case AgentMessage.BodyOneofCase.UpdateRpcd:
                    var update = registry.OnUpdateProgress(connection, message.UpdateRpcd);
                    if (update != null)
                        LogUpdate(hello, update);
                    break;
                case AgentMessage.BodyOneofCase.Metrics:
                    int delivered = registry.OnMetrics(connection, message.Metrics);
                    logger.LogDebug("Agent {MachineId} uploaded {Count} metrics run(s) to {Watchers} watcher(s)", hello.MachineId, message.Metrics.Runs.Count, delivered);
                    break;
                case AgentMessage.BodyOneofCase.Alerts:
                    int alertWatchers = registry.OnAlerts(connection, message.Alerts);
                    logger.LogDebug("Agent {MachineId} uploaded {Count} alert(s) to {Watchers} watcher(s)", hello.MachineId, message.Alerts.Alerts.Count, alertWatchers);
                    break;
                case AgentMessage.BodyOneofCase.Hello:
                    logger.LogWarning("Agent {MachineId} sent a second AgentHello; ignored", hello.MachineId);
                    break;
            }
        }

        private async Task PumpCommandsAsync(AgentConnection connection, IServerStreamWriter<HubCommand> responseStream, CancellationTokenSource streamCts)
        {
            try
            {
                await foreach (var command in connection.Commands.ReadAllAsync(streamCts.Token).ConfigureAwait(false))
                    await responseStream.WriteAsync(command, streamCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (streamCts.IsCancellationRequested)
            {
                // The hub call ended or this stream was replaced.
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Sending a command to agent {MachineId} failed; closing its stream", connection.MachineId);
                await streamCts.CancelAsync().ConfigureAwait(false);
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
                case LaunchStage.StageConsentTimedOut:
                    AgentEvents.Info(logger, Category, $"Diagnostics consent went unanswered on {where}: {progress.Detail}");
                    break;
                case LaunchStage.StageConditionsNotMet:
                    AgentEvents.Info(logger, Category, $"Scheduled diagnostics not started on {where}: {progress.Detail}");
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

        private void LogUpdate(AgentHello hello, UpdateRpcdProgress progress)
        {
            string where = $"{hello.MachineName} ({hello.MachineId}), request {progress.RequestId}";
            if (!AgentRegistry.IsUpdateTerminal(progress))
            {
                logger.LogDebug("RPCD update {Stage} on {Where}: {Detail}", progress.Stage, where, progress.Detail);
                return;
            }

            string message = $"RPCD update {progress.Stage} on {where}: {FirstSentence(progress.Error, progress.Detail)}";
            if (progress.Stage is "failed" or "refused")
                AgentEvents.Warn(logger, message);
            else
                AgentEvents.Info(logger, Category, message);
        }

        private static string FirstSentence(string? error, string? detail)
        {
            if (!string.IsNullOrWhiteSpace(error))
                return error;
            return detail ?? string.Empty;
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
