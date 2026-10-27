using System.Collections.Concurrent;
using System.Threading.Channels;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.Extensions.Logging.Abstractions;
using PeakSWC.RemoteWebView;
using PeakSWC.RemoteWebView.RemoteStaticFiles;

namespace StaticContentCache.Tests;

public class BugReviewTests
{
    [Fact]
    public async Task One_faulted_registration_does_not_hide_the_other_clients()
    {
        var services = new ConcurrentDictionary<string, TaskCompletionSource<ServiceState>>();
        var goodId = Guid.NewGuid().ToString();
        var good = new TaskCompletionSource<ServiceState>(TaskCreationOptions.RunContinuationsAsynchronously);
        good.SetResult(new ServiceState(NullLogger<RemoteWebViewService>.Instance, false)
        {
            Id = goodId,
            HostName = "host-a"
        });
        services[goodId] = good;

        var failed = new TaskCompletionSource<ServiceState>(TaskCreationOptions.RunContinuationsAsynchronously);
        failed.SetException(new InvalidOperationException("registration failed"));
        services[Guid.NewGuid().ToString()] = failed;

        var ipc = new ClientIPCService(
            NullLogger<ClientIPCService>.Instance,
            new ConcurrentDictionary<string, Channel<string>>(),
            services,
            null!,
            new RemoteFilesOptions());

        var response = await ipc.GetServerStatus(new Empty(), null!);

        Assert.Contains(response.ConnectionResponses, client => client.Id == goodId);
    }

    [Fact]
    public async Task Removing_a_client_finishes_an_open_file_read()
    {
        var manager = new ServerFileSyncManager(NullLogger<ServerFileSyncManager>.Instance);
        try
        {
            var id = Guid.NewGuid().ToString();
            var writer = new FakeFileWriter();
            manager.RegisterClient(id, "wwwroot/index.html");
            manager.AssociateResponseStream(id, writer);

            var data = await manager.RequestFileDataAsync(id, "wwwroot/app.js", NullLogger<RemoteFileResolver>.Instance);
            await WaitForRequest(writer);
            var read = data.Pipe.Reader.ReadAsync().AsTask();

            manager.RemoveClient(id);

            var finished = await Task.WhenAny(read, Task.Delay(TimeSpan.FromMilliseconds(500)));
            Assert.Same(read, finished);
        }
        finally
        {
            manager.Dispose();
        }
    }

    [Fact]
    public async Task File_read_for_an_unknown_client_finishes_with_an_error()
    {
        var manager = new ServerFileSyncManager(NullLogger<ServerFileSyncManager>.Instance);
        try
        {
            var data = await manager.RequestFileDataAsync(Guid.NewGuid().ToString(), "wwwroot/app.js", NullLogger<RemoteFileResolver>.Instance);
            Task read;
            try
            {
                read = data.Pipe.Reader.ReadAsync().AsTask();
            }
            catch (Exception ex)
            {
                read = Task.FromException(ex);
            }

            var finished = await Task.WhenAny(read, Task.Delay(TimeSpan.FromMilliseconds(500)));
            Assert.Same(read, finished);
            await Assert.ThrowsAnyAsync<Exception>(async () => await read);
        }
        finally
        {
            manager.Dispose();
        }
    }

    private static async Task<ServerFileReadRequest> WaitForRequest(FakeFileWriter writer)
    {
        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (writer.Requests.Count == 0)
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("The file request was not written.");
            await Task.Delay(10);
        }

        return writer.Requests[0];
    }

    private sealed class FakeFileWriter : IServerStreamWriter<ServerFileReadRequest>
    {
        public WriteOptions? WriteOptions { get; set; }
        public List<ServerFileReadRequest> Requests { get; } = new();

        public Task WriteAsync(ServerFileReadRequest message) => WriteAsync(message, CancellationToken.None);

        public Task WriteAsync(ServerFileReadRequest message, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(message);
            return Task.CompletedTask;
        }
    }
}
