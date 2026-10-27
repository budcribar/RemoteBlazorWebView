using System.Collections.Concurrent;
using System.Text;
using System.Threading.Channels;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.Extensions.Logging.Abstractions;
using PeakSWC.RemoteWebView;
using PeakSWC.RemoteWebView.Pages;
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

    [Fact]
    public async Task Host_page_keeps_characters_outside_ascii()
    {
        var manager = new ServerFileSyncManager(NullLogger<ServerFileSyncManager>.Instance);
        try
        {
            var id = Guid.NewGuid().ToString();
            var writer = new FakeFileWriter();
            manager.RegisterClient(id, "wwwroot/index.html");
            manager.AssociateResponseStream(id, writer);
            var resolver = new RemoteFileResolver(NullLogger<RemoteFileResolver>.Instance, manager);

            var reading = resolver.GetFileStreamAsync(id, "wwwroot/index.html");
            var request = await WaitForRequest(writer);
            var html = "<html><head><base href=\"/\" /> café</head></html>";
            await manager.HandleClientResponse(new ClientFileReadResponse
            {
                ClientId = id,
                RequestId = request.RequestId,
                FileData = new FileData { FileChunk = ByteString.CopyFrom(Encoding.UTF8.GetBytes(html)) }
            });
            await manager.HandleClientResponse(new ClientFileReadResponse
            {
                ClientId = id,
                RequestId = request.RequestId,
                FileData = new FileData { FileChunk = ByteString.Empty }
            });

            var file = await reading.WaitAsync(TimeSpan.FromSeconds(2));
            using var copy = new MemoryStream();
            await file.Stream.CopyToAsync(copy);
            var text = Encoding.UTF8.GetString(copy.ToArray());

            Assert.Contains("café", text);
            Assert.Contains($"/{id}/", text);
        }
        finally
        {
            manager.Dispose();
        }
    }

    [Fact]
    public async Task Modules_manifest_is_read_from_the_client()
    {
        var clientBytes = Encoding.UTF8.GetBytes("[{\"name\":\"App\"}]");
        var (metadata, served) = await ReadModulesManifestFromClient(clientBytes, lastModified: 1_725_000_000);

        Assert.Equal(clientBytes.Length, metadata.Length);
        Assert.Equal(clientBytes, served);
        Assert.NotEqual(Encoding.ASCII.GetBytes("[]"), served);
    }

    // An app can publish its own _framework/blazor.modules.json. The server must
    // return that file's length and bytes, not the two-byte empty array.
    [Fact]
    public async Task Custom_modules_manifest_is_served_as_is()
    {
        var clientBytes = Encoding.UTF8.GetBytes(
            "[{\"name\":\"ModuleA.lib.module.js\"},{\"name\":\"ModuleB.lib.module.js\"},{\"name\":\"ModuleC.lib.module.js\"}]");
        var (metadata, served) = await ReadModulesManifestFromClient(clientBytes, lastModified: 1_725_000_000);

        Assert.Equal(clientBytes.Length, metadata.Length);
        Assert.Equal(clientBytes, served);
        Assert.NotEqual(Encoding.ASCII.GetBytes("[]"), served);
    }

    // The resolver used to skip the client and return LastModified = DateTimeOffset.MinValue.Ticks.
    // The timestamp is now the unix seconds the client sent.
    [Fact]
    public async Task Modules_manifest_timestamp_is_unix_seconds()
    {
        const long clientTimestamp = 1_725_000_000;
        var (metadata, _) = await ReadModulesManifestFromClient(Encoding.UTF8.GetBytes("[]\r\n"), clientTimestamp);

        Assert.Equal(clientTimestamp, metadata.LastModified);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(clientTimestamp), DateTimeOffset.FromUnixTimeSeconds(metadata.LastModified));
    }

    private static async Task<(FileMetadata Metadata, byte[] Served)> ReadModulesManifestFromClient(byte[] clientBytes, long lastModified)
    {
        var manager = new ServerFileSyncManager(NullLogger<ServerFileSyncManager>.Instance);
        try
        {
            var id = Guid.NewGuid().ToString();
            var writer = new FakeFileWriter();
            manager.RegisterClient(id, "wwwroot/index.html");
            manager.AssociateResponseStream(id, writer);
            var resolver = new RemoteFileResolver(NullLogger<RemoteFileResolver>.Instance, manager);
            const string path = "_framework/blazor.modules.json";

            var metadataTask = resolver.GetFileMetaDataAsync(id, path);
            var metadataRequest = await WaitForRequest(writer);
            Assert.Equal(path, metadataRequest.Path);
            Assert.Equal(ServerFileReadRequest.Types.RequestType.MetaData, metadataRequest.RequestType);
            await manager.HandleClientResponse(new ClientFileReadResponse
            {
                ClientId = id,
                RequestId = metadataRequest.RequestId,
                Metadata = new FileMetadata
                {
                    Length = clientBytes.Length,
                    StatusCode = 200,
                    ETag = "client-etag",
                    LastModified = lastModified
                }
            });
            var metadata = await metadataTask.WaitAsync(TimeSpan.FromSeconds(2));

            writer.Requests.Clear();
            var streamTask = resolver.GetFileStreamAsync(id, path);
            var dataRequest = await WaitForRequest(writer);
            Assert.Equal(path, dataRequest.Path);
            Assert.Equal(ServerFileReadRequest.Types.RequestType.FileData, dataRequest.RequestType);
            await manager.HandleClientResponse(new ClientFileReadResponse
            {
                ClientId = id,
                RequestId = dataRequest.RequestId,
                FileData = new FileData { FileChunk = ByteString.CopyFrom(clientBytes) }
            });
            await manager.HandleClientResponse(new ClientFileReadResponse
            {
                ClientId = id,
                RequestId = dataRequest.RequestId,
                FileData = new FileData { FileChunk = ByteString.Empty }
            });

            var file = await streamTask.WaitAsync(TimeSpan.FromSeconds(2));
            using var copy = new MemoryStream();
            await file.Stream.CopyToAsync(copy);
            return (metadata, copy.ToArray());
        }
        finally
        {
            manager.Dispose();
        }
    }

    [Fact]
    public void Restart_page_encodes_the_process_name()
    {
        var html = RestartPage.Html(Guid.NewGuid().ToString(), "app<b>x", "host\"y");

        Assert.DoesNotContain("app<b>x", html);
        Assert.Contains("app&lt;b&gt;x", html);
        Assert.DoesNotContain("host\"y", html);
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
