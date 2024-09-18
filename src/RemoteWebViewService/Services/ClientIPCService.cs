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
    public class ClientIPCService(ILogger<ClientIPCService> logger, ConcurrentDictionary<string, Channel<string>> serviceStateChannel, ConcurrentDictionary<string, ServiceState> serviceDictionary, IUserService userService) : ClientIPC.ClientIPCBase
    {
        private Task WriteResponse(IServerStreamWriter<ClientResponseList> responseStream, IReadOnlyList<string> groups)
        {
            var list = new ClientResponseList();
            list.ClientResponses.AddRange(serviceDictionary.Values.Where(x => groups.Contains(x.Group)).Select(x => new ClientResponse { Markup = x.Markup, Id = x.Id, State = x.InUse ? ClientState.Connected : ClientState.ShuttingDown, Url = x.Url, Group = x.Group }));
            return responseStream.WriteAsync(list);
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



        public override Task<ServerResponse> GetServerStatus(Empty request, ServerCallContext context)
        { 
            List<TaskResponse> GetTaskResponses(string id)
            {
                var responses = new List<TaskResponse>();

                if (serviceDictionary.TryGetValue(id, out ServiceState? ss))
                {
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
                return responses;
            }

            var p = Process.GetCurrentProcess();
            var response = new ServerResponse { Handles = p.HandleCount, PeakWorkingSet = p.PeakWorkingSet64, Threads = p.Threads.Count, WorkingSet = p.WorkingSet64, TotalProcessorTime = p.TotalProcessorTime.TotalSeconds, UpTime = (DateTime.UtcNow - p.StartTime.ToUniversalTime()).TotalSeconds };

            var responses = GetConnectionResponses(serviceDictionary);
            response.ConnectionResponses.AddRange(responses);
            responses.ForEach(x => x.TaskResponses.AddRange(GetTaskResponses(x.Id)));

            return Task.FromResult(response);
        }

        private static List<ConnectionResponse> GetConnectionResponses(ConcurrentDictionary<string, ServiceState> serviceDictionary)
        {
            var snapshot = serviceDictionary.Values.ToList();
           
            return snapshot.Select(x => new ConnectionResponse
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
