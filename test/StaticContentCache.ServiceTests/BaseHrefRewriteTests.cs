using System.Text;
using Google.Protobuf;
using Grpc.Core;
using Microsoft.Extensions.Logging.Abstractions;
using PeakSWC.RemoteWebView;
using PeakSWC.RemoteWebView.RemoteStaticFiles;

namespace StaticContentCache.Tests;

public class BaseHrefRewriteTests
{
    [Fact]
    public async Task Rewriting_the_base_href_keeps_the_rest_of_the_line()
    {
        var manager = new ServerFileSyncManager(NullLogger<ServerFileSyncManager>.Instance);
        try
        {
            var id = Guid.NewGuid().ToString();
            var writer = new LineWriter();
            manager.RegisterClient(id, "wwwroot/index.html");
            manager.AssociateResponseStream(id, writer);
            var resolver = new RemoteFileResolver(NullLogger<RemoteFileResolver>.Instance, manager);

            var reading = resolver.GetFileStreamAsync(id, "wwwroot/index.html");
            var request = await WaitForRequest(writer);
            var html = "<html><head><base href=\"/\" /><link rel=\"stylesheet\" href=\"app.css\" /></head></html>";
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

            Assert.Contains($"<base href=\"/{id}/\"", text);
            Assert.Contains("<link rel=\"stylesheet\" href=\"app.css\" />", text);
        }
        finally
        {
            manager.Dispose();
        }
    }

    private static async Task<ServerFileReadRequest> WaitForRequest(LineWriter writer)
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

    private sealed class LineWriter : IServerStreamWriter<ServerFileReadRequest>
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
