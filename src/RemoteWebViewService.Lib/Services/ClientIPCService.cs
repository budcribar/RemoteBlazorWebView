using System.Reflection;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using PeakSWC.RemoteWebView.Agents;
using PeakSWC.RemoteWebView.RemoteStaticFiles;

namespace PeakSWC.RemoteWebView
{
    public class ClientIPCService(ILogger<ClientIPCService> logger, ConcurrentDictionary<string, Channel<string>> serviceStateChannel, ConcurrentDictionary<string, TaskCompletionSource<ServiceState>> serviceDictionary, IUserService userService, RemoteFilesOptions filesOptions,
        IRelayAuthorization? authorization = null, IAgentTenantPolicy? tenantPolicy = null, AgentOptions? agentOptions = null) : ClientIPC.ClientIPCBase
    {
        /// <summary>What this caller of the relay-wide calls may see (RelayCallerScope): everything, some groups, or nothing.</summary>
        private Task<RelayCallerScope> CallerScope(ServerCallContext context) =>
            RelayCallerScope.ResolveAsync(context, authorization, tenantPolicy, agentOptions, userService);

        private async Task RequireOperator(ServerCallContext context)
        {
            if (!(await CallerScope(context).ConfigureAwait(false)).IsOperator)
                throw new RpcException(new Status(StatusCode.PermissionDenied, "Only a relay operator may do this."));
        }

        private async Task WriteResponse(IServerStreamWriter<ClientResponseList> responseStream, IReadOnlyList<string> groups)
        {
            var list = new ClientResponseList();
            try
            {
                if (groups != null)
                {
                    // Show only complete values
                    var tasks = serviceDictionary.Values.Where(x => x.Task.IsCompletedSuccessfully).Select(x => x.Task);
                    var results = await Task.WhenAll(tasks).ConfigureAwait(false);

                    list.ClientResponses.AddRange(results.Where(x => groups.Contains(x.Group)).Select(x => new ClientResponse { Markup = x.Markup, Id = x.Id, State = x.InUse ? ClientState.Connected : ClientState.ShuttingDown, Url = x.Url, Group = x.Group }));
                }
            }
            catch { }
           
            await responseStream.WriteAsync(list).ConfigureAwait(false);
        }

        public override async Task GetClients(UserMessageRequest request, IServerStreamWriter<ClientResponseList> responseStream, ServerCallContext context)
        {
            string id = request?.Id ?? string.Empty;
            var channel = Channel.CreateUnbounded<string>();
            string key = !string.IsNullOrEmpty(id) && serviceStateChannel.TryAdd(id, channel) ? id : $"{id}:{Guid.NewGuid():N}";
            if (key != id) serviceStateChannel.TryAdd(key, channel);
           
            try
            {
                var groups = await userService.GetUserGroups(CallerOid(request?.Oid, context)).ConfigureAwait(false) ?? [];
                
                await WriteResponse(responseStream, groups).ConfigureAwait(false);

                await foreach (var state in channel.Reader.ReadAllAsync(context.CancellationToken).ConfigureAwait(false))
                {
                    logger.LogDebug($"Client IPC: {state}");
                    await WriteResponse(responseStream, groups).ConfigureAwait(false);
                }
            }
            finally 
            {
                serviceStateChannel.TryRemove(KeyValuePair.Create(key, channel));
            }        
        }

        public override async Task<UserResponse> GetUserGroups(UserRequest request, ServerCallContext context)
        {
            var groups = await userService.GetUserGroups(CallerOid(request?.Oid, context)).ConfigureAwait(false) ?? [];
            var response = new UserResponse();
            response.Groups.AddRange(groups);
            return response;
        }

        /// <summary>
        /// The user whose groups decide what <see cref="GetClients"/> lists and what <see cref="GetUserGroups"/> returns.
        /// When the relay requires sign-in, that is the signed-in caller (the ticket or sign-in on this call) and the oid in
        /// the request is ignored; a call that is not signed in is refused. Without sign-in (<see cref="NoAuthorization"/>)
        /// there is nobody to check, so the request's oid is used as before.
        /// </summary>
        private string CallerOid(string? requested, ServerCallContext context)
        {
            if (authorization?.Enabled != true)
                return requested ?? string.Empty;

            var user = CallContexts.Http(context)?.User;
            var oid = user?.Identity?.IsAuthenticated == true ? user.GetUserObjectId() : null;
            if (string.IsNullOrEmpty(oid))
                throw new RpcException(new Status(StatusCode.Unauthenticated, "Sign in to continue."));
            return oid;
        }

        public override async Task<Empty> SetCache(CacheRequest request, ServerCallContext context)
        {
            await RequireOperator(context).ConfigureAwait(false);

            if (request.EnableServerCache != null)
            {
                filesOptions.UseServerCache = request.EnableServerCache.Value;
                Console.WriteLine($"Server cache enabled = {request.EnableServerCache.Value}");
            }
           

            if (request.EnableClientCache != null)
            {
                filesOptions.UseClientCache = request.EnableClientCache.Value;
                Console.WriteLine($"Client cache enabled = {request.EnableClientCache.Value}");
            }

            return new Empty();
        }


        // The relay's own version (Directory.Build.props VersionPrefix, plus +<commit> when the Docker build passes SourceRevisionId).
        private static readonly string ServerVersionString =
            typeof(ClientIPCService).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? typeof(ClientIPCService).Assembly.GetName().Version?.ToString() ?? "unknown";

        /// <summary>
        /// The relay's own figures and its sessions. An operator (and any caller on a relay without sign-in) sees every
        /// session; a shop sees only its own; a caller who is not signed in is refused.
        /// </summary>
        public override async Task<ServerResponse> GetServerStatus(Empty request, ServerCallContext context)
        {
            var scope = await CallerScope(context).ConfigureAwait(false);
            if (scope.IsNobody)
                throw new RpcException(new Status(StatusCode.PermissionDenied, "Sign in to see the relay's status."));

            var p = Process.GetCurrentProcess();
            var response = new ServerResponse
            {
                ServerVersion = ServerVersionString,
                Handles = p.HandleCount,
                PeakWorkingSet = p.PeakWorkingSet64,
                Threads = p.Threads.Count,
                WorkingSet = p.WorkingSet64,
                TotalProcessorTime = p.TotalProcessorTime.TotalSeconds,
                UpTime = (DateTime.UtcNow - p.StartTime.ToUniversalTime()).TotalSeconds,
                ClientCacheEnabled = filesOptions.UseClientCache,
                ServerCacheEnabled = filesOptions.UseServerCache
            };

            var responses = await GetConnectionResponses(serviceDictionary, scope).ConfigureAwait(false);
            response.ConnectionResponses.AddRange(responses);

            foreach (var connection in responses)
                connection.TaskResponses.AddRange(await GetTaskResponses(serviceDictionary, connection.Id).ConfigureAwait(false));

            return response;
        }

        private static async Task<List<TaskResponse>> GetTaskResponses(ConcurrentDictionary<string, TaskCompletionSource<ServiceState>> serviceDictionary, string id)
        {
            var responses = new List<TaskResponse>();

            if (!serviceDictionary.TryGetValue(id, out var serviceStateTaskSource) || serviceStateTaskSource == null)
                return responses;

            try
            {
                var ss = await serviceStateTaskSource.Task.WaitAsync(TimeSpan.FromSeconds(60), CancellationToken.None).ConfigureAwait(false);

                if (ss.PingTask != null)
                    responses.Add(new TaskResponse { Name = "Ping", Status = (TaskStatus)(int)ss.PingTask.Status });
                if (ss.IPC.BrowserTask != null)
                    responses.Add(new TaskResponse { Name = "Browser", Status = (TaskStatus)(int)ss.IPC.BrowserTask.Status });
                if (ss.IPC.ClientTask != null)
                    responses.Add(new TaskResponse { Name = "Client", Status = (TaskStatus)(int)ss.IPC.ClientTask.Status });
                if (ss.FileReaderTask != null)
                    responses.Add(new TaskResponse { Name = "FileReader", Status = (TaskStatus)(int)ss.FileReaderTask.Status });
                if (ss.IPC.ProcessMessagesTask != null)
                    responses.Add(new TaskResponse { Name = "ProcessMessages", Status = (TaskStatus)(int)ss.IPC.ProcessMessagesTask.Status });
            }
            catch
            {
                // A faulted registration must not fail the whole status response.
            }
            return responses;
        }

        private static async Task<List<ConnectionResponse>> GetConnectionResponses(ConcurrentDictionary<string, TaskCompletionSource<ServiceState>> serviceDictionary, RelayCallerScope scope)
        {
            var snapshotTasks = serviceDictionary.Values.Where(static pending => pending.Task.IsCompleted).ToList();
            var results = new List<ServiceState>();
            foreach (var pending in snapshotTasks)
            {
                try
                {
                    results.Add(await pending.Task.WaitAsync(TimeSpan.FromSeconds(60)).ConfigureAwait(false));
                }
                catch
                {
                    // A faulted or timed-out registration must not drop every other client.
                }
            }

            return results.Where(x => scope.Sees(x.Group)).Select(x => new ConnectionResponse
            {
                HostName = x.HostName,
                Id = x.Id,
                InUse = x.InUse,
                UserName = x.User,
                TotalFilesRead = x.TotalFilesRead,
                TotalReadTime = x.TotalFileReadTime.TotalSeconds,
                TotalBytesRead = x.TotalBytesRead,
                MaxFileReadTime = x.MaxFileReadTime.TotalSeconds,
                TimeConnected = (DateTime.UtcNow - x.StartTime).TotalSeconds
            }).ToList();
        }

        /// <summary>The relay's log: every shop's sessions are in it, so only an operator may read it.</summary>
        public override async Task<LoggedEventResponse> GetLoggedEvents(Empty request, ServerCallContext context)
        {
            await RequireOperator(context).ConfigureAwait(false);
            return await ReadLoggedEvents().ConfigureAwait(false);
        }

        private static Task<LoggedEventResponse> ReadLoggedEvents()
        {
            if (!OperatingSystem.IsWindows())
            {
                var linuxResponse = new LoggedEventResponse();
                linuxResponse.EventResponses.AddRange(InMemoryEventLog.GetEventResponses());
                return Task.FromResult(linuxResponse);
            }

            List<EventResponse> GetEventResponses()
            {
                using var eventLog = new EventLog("Application");

                var processStartTime = Process.GetCurrentProcess().StartTime.ToUniversalTime();
                var cutoffTime = DateTime.UtcNow.Subtract(processStartTime);
                var entries = eventLog.Entries.Cast<EventLogEntry>()
                    .Where(x => x.TimeGenerated.ToUniversalTime() > processStartTime && x.Source == "RemoteWebViewService")
                    .OrderByDescending(x => x.TimeGenerated);

                var results = new List<EventResponse>();
                foreach (var entry in entries)
                {
                    var er = new EventResponse
                    {
                        Timestamp = Timestamp.FromDateTime(entry.TimeGenerated.ToUniversalTime())
                    };
                    er.Messages.AddRange(entry.Message.Split(Environment.NewLine));
                    results.Add(er);
                }

                return results;
            }


            var response = new LoggedEventResponse();
            response.EventResponses.AddRange(GetEventResponses());

            return Task.FromResult(response);
        }

    }
}
