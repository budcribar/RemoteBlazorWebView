using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace PeakSWC.RemoteWebView
{
    public class ClientIPCService(ILogger<ClientIPCService> logger, ConcurrentDictionary<string, Channel<string>> serviceStateChannel, ConcurrentDictionary<string, TaskCompletionSource<ServiceState>> serviceDictionary, IUserService userService, RemoteFilesOptions filesOptions) : ClientIPC.ClientIPCBase
    {
        private async Task WriteResponse(IServerStreamWriter<ClientResponseList> responseStream, IReadOnlyList<string> groups)
        {
            var list = new ClientResponseList();
            try
            {
                var serviceStateTaskSource = serviceDictionary.Values.ToList();
                var tasks = serviceDictionary.Values.Select(x => x.Task.WaitWithTimeout(TimeSpan.FromSeconds(60)));
                var results = await Task.WhenAll(tasks);

                list.ClientResponses.AddRange(results.Where(x => groups.Contains(x.Group)).Select(x => new ClientResponse { Markup = x.Markup, Id = x.Id, State = x.InUse ? ClientState.Connected : ClientState.ShuttingDown, Url = x.Url, Group = x.Group }));
            }
            catch { }
           
            await responseStream.WriteAsync(list);
        }

        public override async Task GetClients(UserMessageRequest request, IServerStreamWriter<ClientResponseList> responseStream, ServerCallContext context)
        {
            string id = request.Id;
            serviceStateChannel.TryAdd(id, Channel.CreateUnbounded<string>());
           
            try
            {
                var groups = await userService.GetUserGroups(request.Oid).ConfigureAwait(false);
                
                await WriteResponse(responseStream, groups).ConfigureAwait(false);

                await foreach (var state in serviceStateChannel[id].Reader.ReadAllAsync(context.CancellationToken).ConfigureAwait(false))
                {
                    logger.LogInformation($"Client IPC: {state}");
                    await WriteResponse(responseStream, groups).ConfigureAwait(false);
                }
            }
            finally 
            {
                serviceStateChannel.Remove(id, out _);
            }        
        }

        public override async Task<UserResponse> GetUserGroups(UserRequest request, ServerCallContext context)
        {
            var groups = await userService.GetUserGroups(request.Oid).ConfigureAwait(false);
            var response = new UserResponse();
            response.Groups.AddRange(groups);
            return response;
        }

        public override Task<Empty> SetCache(CacheRequest request, ServerCallContext context)
        {
            if (request.EnableServerCache != null)
            {
                filesOptions.UseServerCache = request.EnableServerCache.Value;
                Console.WriteLine($"Server cache enabled = {request.EnableServerCache.Value}");
            }
           

            if (request.EnableClientCache != null)
            {
                filesOptions.UseClientCache = request.EnableClientCache.Value;
                Console.WriteLine($"Server cache enabled = {request.EnableClientCache.Value}");
            }
            
            return Task.FromResult(new Empty());
        }


        public override async Task<ServerResponse> GetServerStatus(Empty request, ServerCallContext context)
        { 
            async Task<List<TaskResponse>> GetTaskResponses(string id)
            {
                var responses = new List<TaskResponse>();

                var serviceStateTaskSource = serviceDictionary.GetOrAdd(id, _ => new TaskCompletionSource<ServiceState>(TaskCreationOptions.RunContinuationsAsynchronously));
                try
                {
                    var ss = await serviceStateTaskSource.Task.WaitWithTimeout(TimeSpan.FromSeconds(60));

                   
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
                catch { }
                return responses;
            }

            var p = Process.GetCurrentProcess();
            var response = new ServerResponse { Handles = p.HandleCount, PeakWorkingSet = p.PeakWorkingSet64, Threads = p.Threads.Count, WorkingSet = p.WorkingSet64, TotalProcessorTime = p.TotalProcessorTime.TotalSeconds, UpTime = (DateTime.UtcNow - p.StartTime.ToUniversalTime()).TotalSeconds };

            response.ClientCacheEnabled = filesOptions.UseClientCache;
            response.ServerCacheEnabled = filesOptions.UseServerCache;

            var responses = await GetConnectionResponses(serviceDictionary);
            response.ConnectionResponses.AddRange(responses);

            responses.ForEach(async x => x.TaskResponses.AddRange(await GetTaskResponses(x.Id)));

            return response;
        }

        private static async Task<List<ConnectionResponse>> GetConnectionResponses(ConcurrentDictionary<string, TaskCompletionSource<ServiceState>> serviceDictionary)
        {
            var snapshotTasks = serviceDictionary.Values.ToList();

            try
            {
                var tasks = snapshotTasks.Select( x =>  x.Task.WaitWithTimeout(TimeSpan.FromSeconds(60)));

                var results = await Task.WhenAll(tasks);
                return results.Select(x => new ConnectionResponse
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
            catch { }
            return new();
        }

        public override Task<LoggedEventResponse> GetLoggedEvents(Empty request, ServerCallContext context)
        {
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
