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

#pragma warning disable CA1416

namespace PeakSWC.RemoteWebView
{
    public class ClientIPCService : ClientIPC.ClientIPCBase
    {
        private readonly ILogger<ClientIPCService> _logger;
        private readonly ConcurrentDictionary<string,Channel<string>> _serviceStateChannel;
        private ConcurrentDictionary<string, ServiceState> ServiceDictionary { get; init; }
        private readonly IUserService _userService;

        private Task WriteResponse(IServerStreamWriter<ClientResponseList> responseStream, List<string> groups)
        {
            var list = new ClientResponseList();
            list.ClientResponses.AddRange(ServiceDictionary.Values.Where(x => groups.Contains(x.Group)).Select(x => new ClientResponse { Markup = x.Markup, Id = x.Id, State = x.InUse ? ClientState.Connected : ClientState.ShuttingDown, Url = x.Url, Group = x.Group }));
            return responseStream.WriteAsync(list);
        }

        public ClientIPCService(ILogger<ClientIPCService> logger, ConcurrentDictionary<string,Channel<string>> serviceStateChannel, ConcurrentDictionary<string, ServiceState> serviceDictionary, IUserService userService)
        {
            _logger = logger;
            _serviceStateChannel = serviceStateChannel;
            ServiceDictionary = serviceDictionary;
            _userService = userService;
        }

        public override async Task GetClients(UserMessageRequest request, IServerStreamWriter<ClientResponseList> responseStream, ServerCallContext context)
        {
            string id = request.Id;
            _serviceStateChannel.TryAdd(id, Channel.CreateUnbounded<string>());
            // https://stackoverflow.com/questions/48385996/platformnotsupported-exception-when-calling-adduserasync-net-core-2-0
           
            try
            {
                var groups = await _userService.GetUserGroups(request.Oid);
                
                // If a user is not in any groups then they are defaulted to the "test" group
                if (!groups.Any())
                    groups.Add("test");

                await WriteResponse(responseStream, groups);

                await foreach (var state in _serviceStateChannel[id].Reader.ReadAllAsync(context.CancellationToken))
                {
                    this._logger.LogInformation($"Client IPC: {state}");
                    await WriteResponse(responseStream, groups);
                }
            }
            finally 
            {
                _serviceStateChannel.Remove(id, out _);
            }        
        }

        public override Task<ServerResponse> GetServerStatus(Empty request, ServerCallContext context)
        {
            List<ConnectionResponse> GetConnectionResponses() 
            {
                return ServiceDictionary.Values.Select(x => new ConnectionResponse { HostName=x.HostName, Id=x.Id, InUse=x.InUse, UserName=x.User, TotalFilesRead=x.TotalFilesRead, TotalReadTime=x.TotalFileReadTime.TotalSeconds, TotalBytesRead=x.TotalBytesRead, MaxFileReadTime=x.MaxFileReadTime.TotalSeconds, TimeConnected=DateTime.Now.Subtract(x.StartTime).TotalSeconds }).ToList();
            }
            List<TaskResponse> GetTaskResponses(string id)
            {
                var responses = new List<TaskResponse>();

                if (ServiceDictionary.TryGetValue(id, out ServiceState? ss))
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
           
            List<string> ToList(string s)
            {
                return s.Split(Environment.NewLine).ToList();
            }
           
            List<EventResponse> GetEventResponses()
            {
                EventLog eventLog = new EventLog();
                eventLog.Log = "Application";
               
                var elapsedTime = DateTime.Now.Subtract(Process.GetCurrentProcess().StartTime);
                var entries = eventLog.Entries.Cast<EventLogEntry>().Where(x => x.TimeGenerated > DateTime.Now.Subtract(elapsedTime) /*&& x.Source == "RemoteWebViewService"*/).OrderByDescending(x => x.TimeGenerated);

                List<EventResponse> results = new();
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
