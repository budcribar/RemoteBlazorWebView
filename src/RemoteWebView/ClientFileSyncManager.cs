using Grpc.Core;
using PeakSWC.RemoteWebView;
using Microsoft.Extensions.Logging;
using Google.Protobuf;
using System;
using System.IO;
using System.Threading.Tasks;
using System.Buffers;
using System.Net;
using System.Threading.Channels;
using System.Threading;
using Microsoft.Extensions.FileProviders;
using System.Collections.Generic;
using System.Collections.Concurrent;

namespace FileSyncClient.Services
{
    public class ClientFileSyncManager
    {
        private readonly WebViewIPC.WebViewIPCClient _client;
        private readonly ILogger _logger;
        private readonly string _clientGuid;
        private readonly string _htmlHostPath;
        private readonly AsyncDuplexStreamingCall<ClientFileReadResponse, ServerFileReadRequest> _call;
        private readonly IFileProvider _fileProvider;
        private readonly Action<Exception> _errorCallback;
       
        //private readonly Channel<ClientFileReadResponse> _channel = Channel.CreateBounded<ClientFileReadResponse>(Environment.ProcessorCount);
        //private readonly Channel<ClientFileReadResponse> _channel = Channel.CreateBounded<ClientFileReadResponse>(1);
        private readonly Channel<ClientFileReadResponse> _channel = Channel.CreateUnbounded<ClientFileReadResponse>();
        public ClientFileSyncManager(WebViewIPC.WebViewIPCClient client, Guid clientId, string htmlHostPath, IFileProvider fileProvider, Action<Exception> onException, ILogger logger)
        {
            _client = client;
            _logger = logger;
            _clientGuid = clientId.ToString();
            _htmlHostPath = htmlHostPath;
            _fileProvider = fileProvider;
            _errorCallback = onException;

            // Initiate the duplex streaming call
            _call = _client.RequestClientFileRead();
           
        }

        /// <summary>
        /// Starts handling requests from the server.
        /// </summary>
        public void HandleServerRequests(CancellationToken ct)
        {
            

            // Start a task to consume from the channel and write to the stream
            _ = Task.Factory.StartNew(async () =>
            {
                await foreach (var request in _channel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
                {
                    await _call.RequestStream.WriteAsync(request).ConfigureAwait(false);
                }
            }, ct, TaskCreationOptions.LongRunning, TaskScheduler.Default);

            // Start reading requests from the server
            _ = Task.Run(async () =>
            {
                try
                {
                    await SendInitResponse(_clientGuid, _htmlHostPath);
                    _logger.LogInformation($"Sent Init response to server with clientGuid: {_clientGuid}");

                    await Parallel.ForEachAsync(_call.ResponseStream.ReadAllAsync(ct),
                       new ParallelOptions { CancellationToken = ct/*, MaxDegreeOfParallelism=1*/ },
                       async (request, token) =>
                    {
                        switch (request.RequestType)
                        {
                            case ServerFileReadRequest.Types.RequestType.MetaData:
                                await HandleMetaDataRequestAsync(request);
                                break;
                            case ServerFileReadRequest.Types.RequestType.FileData:
                                await HandleFileDataRequestAsync(request);
                                break;
                            default:
                                _logger.LogWarning($"Received unknown request type: {request.RequestType}");
                                throw new Exception($"Received unknown request type: {request.RequestType}");
                        }
                    });
                }
                catch (Exception ex) 
                {
                    _errorCallback?.Invoke(ex);
                }
                finally {
                   _channel.Writer.Complete();
                }
            });
        }

        private async Task HandleMetaDataRequestAsync(ServerFileReadRequest request)
        {
            var requestId = request.RequestId;
            // TODO
            // var subPath = request.Path.Replace("wwwroot/", "");

            var subPath = request.Path[(request.Path.IndexOf('/') + 1)..]; 

            _logger.LogInformation($"Received MetaData request (requestId: {requestId}) for file: {subPath}");

            // Retrieve file metadata
            FileMetadata metadata = GetFileMetadata(subPath);
            
            // Create and send the metadata response
            var response = new ClientFileReadResponse
            {
                ClientId = _clientGuid,
                RequestId = requestId,
                Path = subPath,
                Metadata = metadata
            };
            await _channel.Writer.WriteAsync(response).ConfigureAwait(false);
          
            _logger.LogInformation($"Sent metadata for file: {subPath}, requestId: {requestId}");
        }
        private static readonly ConcurrentDictionary<string, SemaphoreSlim> _fileLocks = new();

        private SemaphoreSlim GetFileLock(string filePath)
        {
            return _fileLocks.GetOrAdd(filePath, _ => new SemaphoreSlim(1, 1));
        }

        private async Task HandleFileDataRequestAsync(ServerFileReadRequest request)
        {
            var requestId = request.RequestId;
       
            var subPath = request.Path[(request.Path.IndexOf('/') + 1)..];
            //var fileLock = GetFileLock(subPath);
            //await fileLock.WaitAsync();
            _logger.LogInformation($"Received FileData request (requestId: {requestId}) for file: {subPath}");

            const int chunkSize = 8192; // 8 KB
            byte[] buffer = ArrayPool<byte>.Shared.Rent(chunkSize);

            try
            {
                using var fileStream = _fileProvider.GetFileInfo(subPath).CreateReadStream();

                await SendStatusCode(requestId, subPath, HttpStatusCode.OK);
                // Send FileData messages
                while (true)
                {
                    int bytesRead = await fileStream.ReadAsync(buffer, 0, chunkSize);
                    if (bytesRead == 0) break; // End of file

                    var response = new ClientFileReadResponse
                    {
                        ClientId = _clientGuid,
                        RequestId = requestId,
                        Path = subPath,
                        FileData = new FileData
                        {
                            FileChunk = ByteString.CopyFrom(buffer, 0, bytesRead),
                        }
                    };
                    await _channel.Writer.WriteAsync(response).ConfigureAwait(false);
                   
                    _logger.LogInformation($"Sent file chunk of size {bytesRead} bytes for file: {subPath}, requestId: {requestId}");
                }

                await SendCompletionResponse(requestId, subPath);
            }
            catch (FileNotFoundException)
            {
                _logger.LogWarning($"File '{subPath}' not found in client's cache.");
                await SendStatusCode(requestId, subPath, HttpStatusCode.NotFound);
            }
            catch (UnauthorizedAccessException)
            {
                _logger.LogError($"Access denied to file '{subPath}'.");
                await SendStatusCode(requestId, subPath, HttpStatusCode.Forbidden);
            }
            catch (IOException ex)
            {
                _logger.LogError(ex, $"IO error reading file '{subPath}'.");
                await SendStatusCode(requestId, subPath, HttpStatusCode.InternalServerError);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Unexpected error reading file '{subPath}'.");
                await SendStatusCode(requestId, subPath, HttpStatusCode.InternalServerError);
            }
            finally
            {
                //fileLock.Release();
                //// Clean up the semaphore if no one else needs it
                //if (fileLock.CurrentCount == 1)
                //{
                //    _fileLocks.TryRemove(subPath, out _);
                //}

                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        private async Task SendCompletionResponse(string requestId, string relativeFilePath)
        {
            var completionResponse = new ClientFileReadResponse
            {
                ClientId = _clientGuid,
                RequestId = requestId,
                Path = relativeFilePath,
                FileData = new FileData
                {
                    FileChunk = ByteString.Empty,
                }
            };
            await _channel.Writer.WriteAsync(completionResponse).ConfigureAwait(false);
            _logger.LogInformation($"Completed file data transfer for file: {relativeFilePath}, requestId: {requestId}");
        }
       
        private async Task SendInitResponse(string clientGuid, string htmlHostPath)
        {
            var initResponse = new ClientFileReadResponse
            {
                ClientId = clientGuid,
                RequestId = new Guid().ToString(),
                Path = string.Empty,
                Init = new Init { HtmlHostPath = htmlHostPath }
            };
            await _channel.Writer.WriteAsync(initResponse).ConfigureAwait(false);
        }

        private async Task SendStatusCode(string requestId, string relativeFilePath, HttpStatusCode statusCode)
        {
            var statusResponse = new ClientFileReadResponse
            {
                ClientId = _clientGuid,
                RequestId = requestId,
                Path = relativeFilePath,
                FileDataStatus = new FileDataStatus
                {
                    StatusCode = (int)statusCode
                }
            };
            await _channel.Writer.WriteAsync(statusResponse).ConfigureAwait(false);
        }

        private FileMetadata GetFileMetadata(string localFilePath)
        {
            try
            {
                var fileInfo = _fileProvider.GetFileInfo(localFilePath);

                if (!fileInfo.Exists)
                {
                    return new FileMetadata
                    {
                        Length = -1,
                        StatusCode = 404 // File not found
                    };               
                }
               
                return new FileMetadata
                {
                    Length = fileInfo.Length,
                    LastModified = fileInfo.LastModified.ToUnixTimeSeconds(),
                    StatusCode = 200  // Success
                };
            }
            catch (Exception)
            {
                return new FileMetadata
                {
                    Length = -1,
                    StatusCode = 500, // Internal server error                  
                };
            }
        }

        /// <summary>
        /// Closes the gRPC call gracefully.
        /// </summary>
        public async Task CloseAsync()
        {
            await _call.RequestStream.CompleteAsync().ConfigureAwait(false);
            _logger.LogInformation("Closed request stream.");
        }
    }
}
