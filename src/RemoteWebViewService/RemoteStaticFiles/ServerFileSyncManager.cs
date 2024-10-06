using Grpc.Core;
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

namespace PeakSWC.RemoteWebView
{
    public class ServerFileSyncManager : IDisposable
    {
        // Singleton
        private readonly ILogger<ServerFileSyncManager> _logger;
        private readonly ConcurrentDictionary<string, IServerStreamWriter<ServerFileReadRequest>> _clientResponseStreams = new();
        private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, TaskCompletionSource<FileMetadata>>> _metadataRequests = new();
        private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, DataRequest>> _fileDataRequests = new();
        private readonly ConcurrentDictionary<string,string> _htmlHostPaths = new();

        // Optional: set expiration in seconds using environment variables (default to 10 minutes)
        private readonly int _cacheTimeoutSeconds = int.TryParse(
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

            _logger.LogInformation($"Registering new client with GUID: {clientGuid}");
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

            _logger.LogInformation($"Associated response stream for client GUID: {clientGuid}");
        }

        /// <summary>
        /// Processes all write operations from the channel sequentially.
        /// </summary>
        private async Task ProcessWriteChannelAsync()
        {
            await foreach (var writeRequest in _writeChannel.Reader.ReadAllAsync(_cts.Token))
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
                await HandleFileChunkResponse(clientGuid, requestId, response.FileData);
            }
            else
            {
                _logger.LogError($"Received unexpected response type for file from client GUID: {clientGuid}");
            }
        }

        private void HandleMetadataResponse(string clientGuid, string requestId, FileMetadata metadata)
        {
            if (_metadataRequests.TryGetValue(clientGuid, out var clientMetadataRequests))
            {
                if (clientMetadataRequests.TryRemove(requestId, out var tcs))
                {
                    tcs.SetResult(metadata);
                    _logger.LogInformation($"Received metadata for requestId: {requestId} from client GUID: {clientGuid}");
                }
                else
                {
                    _logger.LogError($"No pending metadata request for requestId: {requestId} from client GUID: {clientGuid}");
                }
            }
            else
            {
                _logger.LogError($"No metadata requests mapping found for client GUID: {clientGuid}");
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
                    _logger.LogError($"No pending client file data request for requestId: {requestId} from client GUID: {clientGuid}");
                }
            }
            else
            {
                _logger.LogError($"No file data requests mapping found for client GUID: {clientGuid}");
            }
        }

        public string GetHtmlHostPath(string clientId)
        {
            string defaultHostPath = "index.html";
            if (clientId == null || clientId.Length == 0)
                return defaultHostPath;
            if (_htmlHostPaths.TryGetValue(clientId, out string? hostPath))
                return hostPath == null ? defaultHostPath : hostPath;
            return defaultHostPath;
        }

        /// <summary>
        /// Requests metadata for a specific file from a specific client.
        /// </summary>
        /// <param name="clientGuid">Unique identifier for the client.</param>
        /// <param name="filePath">Relative path of the file.</param>
        /// <returns>FileMetadata object.</returns>
        public Task<FileMetadata> RequestFileMetadataAsync(string clientGuid, string filePath, ILogger<RemoteFileResolver> logger)
        {
            // Generate a unique requestId
            var requestId = Guid.NewGuid().ToString();

            // Create TaskCompletionSource to await the metadata
            var tcs = new TaskCompletionSource<FileMetadata>(TaskCreationOptions.RunContinuationsAsynchronously);

            // Add the TaskCompletionSource to the metadata requests
            var clientMetadataRequests = _metadataRequests.GetOrAdd(clientGuid, new ConcurrentDictionary<string, TaskCompletionSource<FileMetadata>>());
            if (!clientMetadataRequests.TryAdd(requestId, tcs))
            {
                logger.LogCritical($"A metadata request with requestId '{requestId}' for client GUID '{clientGuid}' could not be created.");
                return Task.FromResult(new FileMetadata { Length = -1, StatusCode = (int)HttpStatusCode.InternalServerError });
            }
           
            // Enqueue the write operation to send the metadata request
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
                            RequestType = ServerFileReadRequest.Types.RequestType.MetaData
                        };

                        await responseStream.WriteAsync(request).ConfigureAwait(false);
                        _logger.LogInformation($"Sent metadata request (requestId: {requestId}) for file: {filePath} to client GUID: {clientGuid}");
                    }
                    else
                    {
                        _logger.LogWarning($"Cannot send metadata request. Client GUID '{clientGuid}' is not associated with a response stream.");
                        tcs.SetException(new InvalidOperationException($"Client GUID '{clientGuid}' is not associated with a response stream."));
                    }
                }
            };

            _writeChannel.Writer.TryWrite(writeRequest);

            // Start a timeout to cleanup the request if not completed in time
            CleanupMetadataRequest(clientGuid, requestId, tcs, filePath);

            // Return the task, which will be completed when metadata is received
            return tcs.Task;
        }

        /// <summary>
        /// Requests file data for a specific file from a specific client.
        /// </summary>
        /// <param name="clientGuid">Unique identifier for the client.</param>
        /// <param name="filePath">Relative path of the file.</param>
        /// <returns>MemoryStream containing the file data.</returns>
        public async Task<DataRequest> RequestFileDataAsync(string clientGuid, string filePath, ILogger<RemoteFileResolver> logger)
        {
            // Generate a unique requestId
            var requestId = Guid.NewGuid().ToString();

            // Create a DataRequest to track the file data
            var dataRequest = new DataRequest(_cts.Token);

            // Add the DataRequest to the file data requests
            if (!_fileDataRequests.TryGetValue(clientGuid, out var clientFileDataRequests) || !clientFileDataRequests.TryAdd(requestId, dataRequest))
            {

                logger.LogCritical($"A file data request with requestId '{requestId}' for client GUID '{clientGuid}' could not be created.");
                return dataRequest;
            }

            // Enqueue the write operation to send the file data request
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
                        _logger.LogInformation($"Sent file data request (requestId: {requestId}) for file: {filePath} to client GUID: {clientGuid}");
                    }
                    else
                    {
                        _logger.LogWarning($"Cannot send file data request. Client GUID '{clientGuid}' is not associated with a response stream.");
                        // Complete the PipeWriter with an exception
                        dataRequest.Pipe.Writer.Complete(new InvalidOperationException($"Client GUID '{clientGuid}' is not associated with a response stream."));
                    }
                }
            };

            await _writeChannel.Writer.WriteAsync(writeRequest, dataRequest.CancellationToken).ConfigureAwait(false);

            // Return the DataRequest immediately
            return dataRequest;
        }

        /// <summary>
        /// Cleanup metadata request after a timeout period.
        /// </summary>
        private void CleanupMetadataRequest(string clientGuid, string requestId, TaskCompletionSource<FileMetadata> tcs, string filePath)
        {
            Task.Run(async () =>
            {
                try
                {
                    // Wait for the timeout duration
                    await Task.Delay(_cacheTimeoutSeconds * 1000).ConfigureAwait(false);

                    // If the task is not completed, set an exception
                    if (!tcs.Task.IsCompleted)
                    {
                        tcs.SetException(new TimeoutException(
                            $"Metadata request for file '{filePath}', requestId '{requestId}' from client GUID '{clientGuid}' timed out."));
                        _logger.LogInformation($"Metadata request (requestId: {requestId}) for file '{filePath}' from client GUID '{clientGuid}' timed out.");

                        // Remove the request from the dictionary
                        if (_metadataRequests.TryGetValue(clientGuid, out var clientMetadataRequests))
                        {
                            clientMetadataRequests.TryRemove(requestId, out _);
                            _logger.LogInformation($"Removed timed out metadata request (requestId: {requestId}) for file '{filePath}' from client GUID '{clientGuid}'.");
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Error during cleanup of metadata request (requestId: {requestId}) for file '{filePath}' from client GUID '{clientGuid}'.");
                }
            });
        }  

        /// <summary>
        /// Removes a client from all mappings.
        /// </summary>
        /// <param name="clientGuid">Unique identifier for the client.</param>
        public void RemoveClient(string clientGuid)
        {
            if (_clientResponseStreams.TryRemove(clientGuid, out _))
            {
                _logger.LogInformation($"Removed response stream association for client GUID: {clientGuid}");
            }

            if (_metadataRequests.TryRemove(clientGuid, out _))
            {
                _logger.LogInformation($"Removed all metadata requests for client GUID: {clientGuid}");
            }

            if (_fileDataRequests.TryRemove(clientGuid, out _))
            {
                _logger.LogInformation($"Removed all file data requests for client GUID: {clientGuid}");
            }
           

            if (_htmlHostPaths.TryRemove(clientGuid, out _))
            {
                _logger.LogInformation($"Removed htmlHostPath for client GUID: {clientGuid}");
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
                // Wait for the channel reader to finish processing
                ProcessWriteChannelAsync().Wait();
            }
            catch (AggregateException ae)
            {
                ae.Handle(ex => ex is OperationCanceledException);
            }

            _cts.Dispose();

            // Dispose other disposable resources here
            foreach (var clientFileDataRequests in _fileDataRequests.Values)
            {
                foreach (var dataRequest in clientFileDataRequests.Values)
                {
                    dataRequest.Dispose();
                }
            }

            // Dispose metadata requests if necessary
            foreach (var clientMetadataRequests in _metadataRequests.Values)
            {
                foreach (var tcs in clientMetadataRequests.Values)
                {
                    if (!tcs.Task.IsCompleted)
                    {
                        tcs.SetException(new OperationCanceledException("ServerFileSyncManager is disposing."));
                    }
                }
            }

            _clientResponseStreams.Clear();
            _metadataRequests.Clear();
            _fileDataRequests.Clear();

            _logger.LogInformation("ServerFileSyncManager disposed successfully.");
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
    public class DataRequest : IDisposable
    {
        public Pipe Pipe { get; }

        public CancellationToken CancellationToken { get; }

        public DataRequest(CancellationToken cancellationToken)
        {
            Pipe = new Pipe();
            CancellationToken = cancellationToken;
        }

        public void Dispose()
        {
            Pipe.Writer.Complete();
            Pipe.Reader.Complete();
        }
    }

}
