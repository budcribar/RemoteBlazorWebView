using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using PeakSWC.RemoteWebView.Agent;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace PeakSWC.RemoteWebView.Agents
{
    /// <summary>AgentAdmin: what the admin site (gRPC-Web) uses to list computers and start diagnostics on one.</summary>
    public class AgentAdminService(ILogger<AgentAdminService> logger, AgentRegistry registry, AgentOptions options) : AgentAdmin.AgentAdminBase
    {
        private const string Category = "PeakSWC.RemoteWebView.Agents.AgentAdminService";
        public const string DefaultTechnician = "A technician";
        public static readonly TimeSpan WatchAgentsInterval = TimeSpan.FromSeconds(15);
        private static int warnedNoAdminKey;

        public override Task<AgentList> GetAgents(Empty request, ServerCallContext context) =>
            Task.FromResult(registry.GetAgentList());

        public override async Task WatchAgents(Empty request, IServerStreamWriter<AgentList> responseStream, ServerCallContext context)
        {
            var token = context.CancellationToken;
            try
            {
                while (!token.IsCancellationRequested)
                {
                    // Take the signal before the snapshot so a change in between still wakes us.
                    var changed = registry.Changed;
                    await responseStream.WriteAsync(registry.GetAgentList()).ConfigureAwait(false);
                    try
                    {
                        await changed.WaitAsync(WatchAgentsInterval, token).ConfigureAwait(false);
                    }
                    catch (TimeoutException)
                    {
                    }
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
            }
        }

        public override Task<StartSessionResponse> StartSession(StartSessionRequest request, ServerCallContext context)
        {
            string technician = GetTechnician(request, context);
            string serverUrl = GetServerUrl(options, context);
            bool adminKeyValid = options.IsAdminKeyValid(GetAdminKey(context));
            if (!options.AdminKeyConfigured && Interlocked.Exchange(ref warnedNoAdminKey, 1) == 0)
                AgentEvents.Warn(logger, $"{AgentOptions.AdminKeyVariable} is not set: every diagnostics start asks the person at the PC for consent");

            var outcome = registry.StartSession(request.MachineId, request.Unattended, technician, serverUrl, options.AdminKeyConfigured, adminKeyValid);
            var response = outcome.Response;
            if (outcome.Denied)
            {
                AgentEvents.Warn(logger, $"Diagnostics request on {request.MachineId} refused: missing or wrong {AgentOptions.AdminKeyHeader}");
                throw new RpcException(new Status(StatusCode.PermissionDenied, response.Error));
            }
            string who = string.IsNullOrEmpty(outcome.MachineName) ? request.MachineId : $"{outcome.MachineName} ({request.MachineId})";

            if (response.Accepted)
                AgentEvents.Info(logger, Category,
                    $"Diagnostics requested on {who} by {technician}: request {response.RequestId}, session {response.SessionId}, " +
                    (outcome.RequireConsent ? "consent required" : "unattended"));
            else
                AgentEvents.Info(logger, Category, $"Diagnostics request on {who} by {technician} refused: {response.Error}");

            return Task.FromResult(response);
        }

        public override async Task WatchLaunch(WatchLaunchRequest request, IServerStreamWriter<LaunchProgress> responseStream, ServerCallContext context)
        {
            using var subscription = registry.SubscribeLaunch(request.RequestId)
                ?? throw new RpcException(new Status(StatusCode.NotFound, "No launch with that request_id"));
            var token = context.CancellationToken;
            try
            {
                await foreach (var progress in subscription.Reader.ReadAllAsync(token).ConfigureAwait(false))
                    await responseStream.WriteAsync(progress).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
            }
        }

        public override Task<Empty> CancelLaunch(CancelCommand request, ServerCallContext context)
        {
            if (options.AdminKeyConfigured && !options.IsAdminKeyValid(GetAdminKey(context)))
                throw new RpcException(new Status(StatusCode.PermissionDenied, ConsentPolicy.AdminKeyRequired));

            var outcome = registry.CancelLaunch(request.RequestId);
            if (outcome == CancelLaunchOutcome.NotFound)
                throw new RpcException(new Status(StatusCode.NotFound, "No launch with that request_id"));
            if (outcome == CancelLaunchOutcome.Queued)
                AgentEvents.Info(logger, Category, $"Cancel requested for launch {request.RequestId}");
            return Task.FromResult(new Empty());
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

        /// <summary>The signed-in user's name when authentication is on, else what the admin site sent, else "A technician".</summary>
        public static string GetTechnician(StartSessionRequest request, ServerCallContext context)
        {
            try
            {
                var user = context.GetHttpContext().User;
                if (user.Identity?.IsAuthenticated == true)
                {
                    string? name = user.FindFirst("name")?.Value ?? user.Identity.Name;
                    if (!string.IsNullOrWhiteSpace(name))
                        return name.Trim();
                }
            }
            catch (InvalidOperationException)
            {
                // Not hosted by ASP.NET Core (unit tests).
            }

            return string.IsNullOrWhiteSpace(request.Technician) ? DefaultTechnician : request.Technician.Trim();
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
            }

            return "https://" + context.Host;
        }
    }
}
