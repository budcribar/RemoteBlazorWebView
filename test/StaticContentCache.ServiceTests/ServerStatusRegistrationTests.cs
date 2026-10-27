using System.Collections.Concurrent;
using System.Threading.Channels;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging.Abstractions;
using PeakSWC.RemoteWebView;
using PeakSWC.RemoteWebView.RemoteStaticFiles;
using PeakSWC.RemoteWebView.Services;

namespace StaticContentCache.Tests;

public class ServerStatusRegistrationTests
{
    [Fact]
    public async Task Status_includes_task_rows_for_a_connected_client()
    {
        var id = Guid.NewGuid().ToString();
        var (ipc, services) = ConnectedClient(id, id);
        var state = await services[id].Task;

        state.PingTask = Task.CompletedTask;

        var response = await ipc.GetServerStatus(new Empty(), null!);

        var connection = Assert.Single(response.ConnectionResponses);
        Assert.Equal(id, connection.Id);
        Assert.Contains(connection.TaskResponses, task => task.Name == "Ping");
    }

    [Fact]
    public async Task Status_does_not_create_a_registration_for_an_id_that_is_not_registered()
    {
        var registeredId = Guid.NewGuid().ToString();
        var missingId = "not-registered";
        var (ipc, services) = ConnectedClient(registeredId, missingId);

        await ipc.GetServerStatus(new Empty(), null!);

        Assert.False(services.ContainsKey(missingId));
        Assert.True(services.ContainsKey(registeredId));
    }

    private static (ClientIPCService Ipc, ConcurrentDictionary<string, TaskCompletionSource<ServiceState>> Services) ConnectedClient(string dictionaryKey, string stateId)
    {
        var services = new ConcurrentDictionary<string, TaskCompletionSource<ServiceState>>();
        var state = new ServiceState(NullLogger<RemoteWebViewService>.Instance, false)
        {
            Id = stateId,
            HostName = "host-a"
        };
        var ready = new TaskCompletionSource<ServiceState>(TaskCreationOptions.RunContinuationsAsynchronously);
        ready.SetResult(state);
        services[dictionaryKey] = ready;

        var ipc = new ClientIPCService(
            NullLogger<ClientIPCService>.Instance,
            new ConcurrentDictionary<string, Channel<string>>(),
            services,
            null!,
            new RemoteFilesOptions());
        return (ipc, services);
    }
}
