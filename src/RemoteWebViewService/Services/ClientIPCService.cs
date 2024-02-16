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
                var groups = await userService.GetUserGroups(request.Oid);
                
                await WriteResponse(responseStream, groups);

                await foreach (var state in serviceStateChannel[id].Reader.ReadAllAsync(context.CancellationToken))
                {
                    logger.LogInformation($"Client IPC: {state}");
                    await WriteResponse(responseStream, groups);
                }
            }
            finally 
            {
                serviceStateChannel.Remove(id, out _);
            }        
        }

        public override async Task<UserResponse> GetUserGroups(UserRequest request, ServerCallContext context)
        {
            var groups = await userService.GetUserGroups(request.Oid);
            var response = new UserResponse();
            response.Groups.AddRange(groups);
            return response;
        }

        public override Task<ServerResponse> GetServerStatus(Empty request, ServerCallContext context)
        {
            List<ConnectionResponse> GetConnectionResponses() 
            {
                return serviceDictionary.Values.Select(x => new ConnectionResponse { HostName=x.HostName, Id=x.Id, InUse=x.InUse, UserName=x.User, TotalFilesRead=x.TotalFilesRead, TotalReadTime=x.TotalFileReadTime.TotalSeconds, TotalBytesRead=x.TotalBytesRead, MaxFileReadTime=x.MaxFileReadTime.TotalSeconds, TimeConnected=DateTime.Now.Subtract(x.StartTime).TotalSeconds }).ToList();
            }
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
                }
                return responses;
            }

            var p = Process.GetCurrentProcess();
            var response = new ServerResponse { Handles = p.HandleCount, PeakWorkingSet=p.PeakWorkingSet64, Threads=p.Threads.Count, WorkingSet=p.WorkingSet64, TotalProcessorTime = p.TotalProcessorTime.TotalSeconds, UpTime = DateTime.Now.Subtract(p.StartTime).TotalSeconds };

            var responses = GetConnectionResponses();
            response.ConnectionResponses.AddRange(responses);
            responses.ForEach(x => x.TaskResponses.AddRange(GetTaskResponses(x.Id)));
         
            return Task.FromResult(response);
        }

        public override Task<LoggedEventResponse> GetLoggedEvents(Empty request, ServerCallContext context)
        {
            List<EventResponse> GetEventResponses()
            {
                using EventLog eventLog = new();
                eventLog.Log = "Application";
               
                var elapsedTime = DateTime.Now.Subtract(Process.GetCurrentProcess().StartTime);
                var entries = eventLog.Entries.Cast<EventLogEntry>().Where(x => x.TimeGenerated > DateTime.Now.Subtract(elapsedTime) && x.Source == "RemoteWebViewService").OrderByDescending(x => x.TimeGenerated);

                List<EventResponse> results = [];
                foreach (var entry in entries) {
                    var er = new EventResponse { Timestamp = Timestamp.FromDateTime(entry.TimeGenerated.ToUniversalTime()) };
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
