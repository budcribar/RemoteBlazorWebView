using Grpc.Core;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Primitives;
using PeakSWC.RemoteWebView;

namespace StaticContentCache.Tests;

public class FileReadCompletionTests
{
    // A normal 404 is returned earlier by metadata when the file does not exist.
    // This is the later read, after that metadata check would already have returned 200.
    [Theory]
    [MemberData(nameof(ReadFailures))]
    public async Task Read_failure_after_metadata_returned_200_sends_empty_completion_normal_404_is_earlier_at_metadata(Exception failure)
    {
        var writer = new ClosedPipeWriter();
        var manager = new ClientFileSyncManager(
            Guid.NewGuid().ToString(),
            new ExistsThenThrowsProvider(failure),
            writer,
            NullLogger.Instance);

        await manager.HandleFileDataRequestForTests(new ServerFileReadRequest
        {
            RequestId = "read-1",
            Path = "wwwroot/app.js",
            RequestType = ServerFileReadRequest.Types.RequestType.FileData
        });

        Assert.Contains(writer.Messages, message => message.FileData != null && message.FileData.FileChunk.Length == 0);
    }

    public static IEnumerable<object[]> ReadFailures()
    {
        yield return new object[] { new FileNotFoundException("missing") };
        yield return new object[] { new IOException("io") };
        yield return new object[] { new UnauthorizedAccessException("denied") };
        yield return new object[] { new InvalidDataException("bad") };
    }

    private sealed class ClosedPipeWriter : IClientStreamWriter<ClientFileReadResponse>
    {
        public WriteOptions? WriteOptions { get; set; }
        public List<ClientFileReadResponse> Messages { get; } = new();

        public Task WriteAsync(ClientFileReadResponse message)
        {
            Messages.Add(message);
            if (message.FileData != null && message.FileData.FileChunk.Length == 0)
                throw new InvalidOperationException("The request stream is already completed.");
            return Task.CompletedTask;
        }

        public Task CompleteAsync() => Task.CompletedTask;
    }

    private sealed class ExistsThenThrowsProvider : IFileProvider
    {
        private readonly Exception _failure;
        public ExistsThenThrowsProvider(Exception failure) => _failure = failure;
        public IDirectoryContents GetDirectoryContents(string subpath) => throw new NotSupportedException();
        public IChangeToken Watch(string filter) => NullChangeToken.Singleton;
        public IFileInfo GetFileInfo(string subpath) => new PresentFile(_failure);

        private sealed class PresentFile : IFileInfo
        {
            private readonly Exception _failure;
            public PresentFile(Exception failure) => _failure = failure;
            public bool Exists => true;
            public long Length => 4;
            public string? PhysicalPath => null;
            public string Name => "app.js";
            public DateTimeOffset LastModified => DateTimeOffset.UnixEpoch;
            public bool IsDirectory => false;
            public Stream CreateReadStream() => throw _failure;
        }
    }
}
