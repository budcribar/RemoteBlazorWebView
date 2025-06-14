using Grpc.Core;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.IO.Pipelines;
using System.Net;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Channel = System.Threading.Channels.Channel;

namespace PeakSWC.RemoteWebView.RemoteStaticFiles
{
    public class ServerFileSyncManager : IDisposable
    {
        // Singleton
        private readonly ILogger<ServerFileSyncManager> _logger;
        private readonly ConcurrentDictionary<string, IServerStreamWriter<ServerFileReadRequest>> _clientResponseStreams = new();
        private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, TaskCompletionSource<FileMetadata>>> _metadataRequests = new();
        private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, DataRequest>> _fileDataRequests = new();
        private readonly ConcurrentDictionary<string, string> _htmlHostPaths = new();

        // Optional: set expiration in seconds using environment variables (default to 10 minutes)
        public readonly int CacheTimeoutSeconds = int.TryParse(
            Environment.GetEnvironmentVariable("CACHE_TIMEOUT_SECONDS"),
            out var timeout) ? timeout : 600;

        // Channel to serialize all write operations
        private static readonly int MaxConcurrentFileRequests = 1000; // Adjust as needed
        private readonly Channel<WriteRequest> _writeChannel = Channel.CreateBounded<WriteRequest>(new BoundedChannelOptions(MaxConcurrentFileRequests)
        {
            SingleReader = true, // Only one consumer
            SingleWriter = false, // Multiple producers
            FullMode = BoundedChannelFullMode.Wait
        });

        private readonly CancellationTokenSource _cts = new CancellationTokenSource();

        public ServerFileSyncManager(ILogger<ServerFileSyncManager> logger)
        {
            _logger = logger;
            // Start the channel reader
            _ = Task.Run(ProcessWriteChannelAsync, _cts.Token);
        }

        /// <summary>
        /// Registers a new client with its unique clientGuid.
        /// </summary>
        /// <param name="clientGuid">Unique identifier for the client.</param>
        public void RegisterClient(string clientGuid, string htmlHostPath)
        {
            if (string.IsNullOrWhiteSpace(clientGuid))
                throw new ArgumentException("Client GUID cannot be null or empty.", nameof(clientGuid));

            _logger.LogDebug("Registering new client with GUID: {ClientGuid}", clientGuid);
            // Initialize the nested dictionaries for the client
            _metadataRequests.TryAdd(clientGuid, new ConcurrentDictionary<string, TaskCompletionSource<FileMetadata>>());
            _fileDataRequests.TryAdd(clientGuid, new ConcurrentDictionary<string, DataRequest>());
            _htmlHostPaths.TryAdd(clientGuid, htmlHostPath);
        }

        /// <summary>
        /// Associates the response stream with the client GUID.
        /// This should be called when establishing the gRPC stream.
        /// </summary>
        /// <param name="clientGuid">Unique identifier for the client.</param>
        /// <param name="responseStream">Server's response stream for the client.</param>
        public void AssociateResponseStream(string clientGuid, IServerStreamWriter<ServerFileReadRequest> responseStream)
        {
            if (!_clientResponseStreams.TryAdd(clientGuid, responseStream))
            {
                throw new InvalidOperationException($"Client with GUID '{clientGuid}' is already associated with a response stream.");
            }

            _logger.LogDebug("Associated response stream for client GUID: {ClientGuid}", clientGuid);
        }

        /// <summary>
        /// Processes all write operations from the channel sequentially.
        /// </summary>
        private async Task ProcessWriteChannelAsync()
        {
            await foreach (var writeRequest in _writeChannel.Reader.ReadAllAsync(_cts.Token).ConfigureAwait(false))
            {
                try
                {
                    await writeRequest.Operation().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing write operation from channel.");
                    // Optionally implement retry logic or other error handling
                }
            }
        }

        /// <summary>
        /// Handles incoming responses from the client.
        /// </summary>
        /// <param name="response">Client's response message.</param>
        public async Task HandleClientResponse(ClientFileReadResponse response)
        {
            var clientGuid = response.ClientId;
            var requestId = response.RequestId;

            if (string.IsNullOrWhiteSpace(clientGuid) || string.IsNullOrWhiteSpace(requestId))
            {
                _logger.LogError("Received response with empty clientId or requestId.");
                return;
            }

            if (response.ResponseCase == ClientFileReadResponse.ResponseOneofCase.Metadata)
            {
                HandleMetadataResponse(clientGuid, requestId, response.Metadata);
            }
            else if (response.ResponseCase == ClientFileReadResponse.ResponseOneofCase.FileData)
            {
                await HandleFileChunkResponse(clientGuid, requestId, response.FileData).ConfigureAwait(false);
            }
            else
            {
                _logger.LogError("Received unexpected response type for file from client GUID: {ClientGuid}", clientGuid);
            }
        }

        private void HandleMetadataResponse(string clientGuid, string requestId, FileMetadata metadata)
        {
            if (_metadataRequests.TryGetValue(clientGuid, out var clientMetadataRequests))
            {
                if (clientMetadataRequests.TryRemove(requestId, out var tcs))
                {
                    tcs.SetResult(metadata);
                    _logger.LogDebug("Received metadata for requestId: {RequestId} from client GUID: {ClientGuid}", requestId, clientGuid);
                }
                else
                {
                    _logger.LogError("No pending metadata request for requestId: {RequestId} from client GUID: {ClientGuid}", requestId, clientGuid);
                }
            }
            else
            {
                _logger.LogError("No metadata requests mapping found for client GUID: {ClientGuid}", clientGuid);
            }
        }

        private async Task HandleFileChunkResponse(string clientGuid, string requestId, FileData fileData)
        {
            if (_fileDataRequests.TryGetValue(clientGuid, out var clientFileDataRequests))
            {
                if (clientFileDataRequests.TryGetValue(requestId, out var dataRequest))
                {
                    var pipeWriter = dataRequest.Pipe.Writer;

                    try
                    {
                        if (fileData.FileChunk.Length > 0)
                        {
                            await pipeWriter.WriteAsync(fileData.FileChunk.Memory, dataRequest.CancellationToken).ConfigureAwait(false);
                            await pipeWriter.FlushAsync(dataRequest.CancellationToken).ConfigureAwait(false);
                        }
                        else
                        {
                            await pipeWriter.CompleteAsync().ConfigureAwait(false);
                            clientFileDataRequests.TryRemove(requestId, out _);
                        }
                    }
                    catch (Exception ex)
                    {
                        try
                        {
                            pipeWriter.Complete(ex);
                        }
                        catch (Exception completeEx)
                        {
                            _logger.LogError(completeEx, "Error completing the pipe writer.");
                        }
                        _logger.LogError(ex, "Error processing file chunk.");
                    }
                }
                else
                {
                    _logger.LogError("No pending client file data request for requestId: {RequestId} from client GUID: {ClientGuid}", requestId, clientGuid);
                }
            }
            else
            {
                _logger.LogError("No file data requests mapping found for client GUID: {ClientGuid}", clientGuid);
            }
        }

        public string GetHtmlHostPath(string clientId)
        {
            const string defaultHostPath = "index.html";
            if (string.IsNullOrEmpty(clientId))
                return defaultHostPath;
            return _htmlHostPaths.TryGetValue(clientId, out var hostPath) && !string.IsNullOrEmpty(hostPath)
                ? hostPath
                : defaultHostPath;
        }

        /// <summary>
        /// Requests metadata for a specific file from a specific client.
        /// </summary>
        public Task<FileMetadata> RequestFileMetadataAsync(string clientGuid, string filePath, ILogger<RemoteFileResolver> logger)
        {
            var requestId = Guid.NewGuid().ToString();
            var tcs = new TaskCompletionSource<FileMetadata>(TaskCreationOptions.RunContinuationsAsynchronously);
            var clientMetadataRequests = _metadataRequests.GetOrAdd(clientGuid, new ConcurrentDictionary<string, TaskCompletionSource<FileMetadata>>());

            if (!clientMetadataRequests.TryAdd(requestId, tcs))
            {
                logger.LogCritical("A metadata request with requestId: {RequestId} for client GUID: {ClientGuid} could not be created.", requestId, clientGuid);
                return Task.FromResult(new FileMetadata { Length = -1, StatusCode = (int)HttpStatusCode.InternalServerError });
            }

            // 1) timeout token instead of CleanupMetadataRequest
            var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(CacheTimeoutSeconds));
            timeoutCts.Token.Register(() =>
            {
                if (tcs.TrySetException(new TimeoutException("Metadata request timed out.")))
                {
                    logger.LogDebug(
                        "Metadata request (requestId: {RequestId}) for file: {FilePath} from client GUID: {ClientGuid} timed out.",
                        requestId, filePath, clientGuid);
                    if (_metadataRequests.TryGetValue(clientGuid, out var dict))
                        dict.TryRemove(requestId, out _);
                }
            }, useSynchronizationContext: false);

            // 2) enqueue write
            _writeChannel.Writer.TryWrite(new WriteRequest
            {
                Operation = async () =>
                {
                    if (_clientResponseStreams.TryGetValue(clientGuid, out var responseStream))
                    {
                        var request = new ServerFileReadRequest
                        {
                            ClientId = clientGuid,
                            RequestId = requestId,
                            Path = filePath,
                            RequestType = ServerFileReadRequest.Types.RequestType.MetaData
                        };
                        await responseStream.WriteAsync(request).ConfigureAwait(false);
                        _logger.LogDebug("Sent metadata request (requestId: {RequestId}) for file: {FilePath} to client GUID: {ClientGuid}", requestId, filePath, clientGuid);
                    }
                    else
                    {
                        tcs.TrySetException(new InvalidOperationException($"Client GUID '{clientGuid}' is not associated with a response stream."));
                    }
                }
            });

            // 3) dispose the CTS when done
            tcs.Task.ContinueWith(_ => timeoutCts.Dispose(), TaskScheduler.Default);

            return tcs.Task;
        }

        /// <summary>
        /// Requests file data for a specific file from a specific client.
        /// </summary>
        public async Task<DataRequest> RequestFileDataAsync(string clientGuid, string filePath, ILogger<RemoteFileResolver> logger)
        {
            var requestId = Guid.NewGuid().ToString();
            var dataRequest = new DataRequest(_cts.Token);

            if (!_fileDataRequests.TryGetValue(clientGuid, out var clientFileDataRequests) || !clientFileDataRequests.TryAdd(requestId, dataRequest))
            {
                logger.LogCritical("A file data request with requestId: {RequestId} for client GUID: {ClientGuid} could not be created.", requestId, clientGuid);
                return dataRequest;
            }

            var writeRequest = new WriteRequest
            {
                Operation = async () =>
                {
                    if (_clientResponseStreams.TryGetValue(clientGuid, out var responseStream))
                    {
                        var request = new ServerFileReadRequest
                        {
                            ClientId = clientGuid,
                            RequestId = requestId,
                            Path = filePath,
                            RequestType = ServerFileReadRequest.Types.RequestType.FileData
                        };

                        await responseStream.WriteAsync(request).ConfigureAwait(false);
                        _logger.LogDebug("Sent file data request (requestId: {RequestId}) for file: {FilePath} to client GUID: {ClientGuid}", requestId, filePath, clientGuid);
                    }
                    else
                    {
                        _logger.LogWarning("Cannot send file data request. Client GUID: {ClientGuid} is not associated with a response stream.", clientGuid);
                        dataRequest.Pipe.Writer.Complete(new InvalidOperationException($"Client GUID '{clientGuid}' is not associated with a response stream."));
                    }
                }
            };

            await _writeChannel.Writer.WriteAsync(writeRequest, dataRequest.CancellationToken).ConfigureAwait(false);
            return dataRequest;
        }

        /// <summary>
        /// Removes a client from all mappings.
        /// </summary>
        /// <param name="clientGuid">Unique identifier for the client.</param>
        public void RemoveClient(string clientGuid)
        {
            if (_clientResponseStreams.TryRemove(clientGuid, out _))
            {
                _logger.LogDebug("Removed response stream association for client GUID: {ClientGuid}", clientGuid);
            }

            if (_metadataRequests.TryRemove(clientGuid, out _))
            {
                _logger.LogDebug("Removed all metadata requests for client GUID: {ClientGuid}", clientGuid);
            }

            if (_fileDataRequests.TryRemove(clientGuid, out _))
            {
                _logger.LogDebug("Removed all file data requests for client GUID: {ClientGuid}", clientGuid);
            }

            if (_htmlHostPaths.TryRemove(clientGuid, out _))
            {
                _logger.LogDebug("Removed htmlHostPath for client GUID: {ClientGuid}", clientGuid);
            }
        }

        /// <summary>
        /// Disposes resources used by the ServerFileSyncManager.
        /// </summary>
        public void Dispose()
        {
            _cts.Cancel();
            _writeChannel.Writer.Complete();

            try
            {
                ProcessWriteChannelAsync().Wait();
            }
            catch (AggregateException ae)
            {
                ae.Handle(ex => ex is OperationCanceledException);
            }

            _cts.Dispose();

            foreach (var clientFileDataRequests in _fileDataRequests.Values)
                foreach (var dataRequest in clientFileDataRequests.Values)
                    dataRequest.Dispose();

            foreach (var clientMetadataRequests in _metadataRequests.Values)
                foreach (var tcs in clientMetadataRequests.Values)
                    if (!tcs.Task.IsCompleted)
                        tcs.SetException(new OperationCanceledException("ServerFileSyncManager is disposing."));

            _clientResponseStreams.Clear();
            _metadataRequests.Clear();
            _fileDataRequests.Clear();

            _logger.LogDebug("ServerFileSyncManager disposed successfully.");
        }
    }

    /// <summary>
    /// Represents a write operation request.
    /// </summary>
    public class WriteRequest
    {
        public Func<Task> Operation { get; set; } = async () => { await Task.CompletedTask; };
    }

    /// <summary>
    /// Represents a data request for file synchronization.
    /// </summary>
    public class DataRequest(CancellationToken cancellationToken) : IDisposable
    {
        public Pipe Pipe { get; } = new Pipe();
        public CancellationToken CancellationToken { get; } = cancellationToken;
        public void Dispose()
        {
            Pipe.Writer.Complete();
            Pipe.Reader.Complete();
        }
    }
}
