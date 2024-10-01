using Grpc.Core;
using PeakSWC.RemoteWebView;
using Microsoft.Extensions.Logging;
using Google.Protobuf;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Buffers;
using System.Net;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;
using System.Linq.Expressions;
using System.IO.Pipelines;
using System.Threading.Channels;
using Microsoft.AspNetCore.Identity.Data;

namespace FileSyncClient.Services
{
    public class ClientFileSyncManager
    {
        private readonly WebViewIPC.WebViewIPCClient _client;
        private readonly ILogger<ClientFileSyncManager> _logger;
        private readonly string _clientGuid;
        private readonly string _htmlHostPath;
        private readonly AsyncDuplexStreamingCall<ClientFileReadResponse, ServerFileReadRequest> _call;

        // Define the client's cache directory
        private readonly string _clientCacheDirectory = Path.Combine(AppContext.BaseDirectory, "client_cache");
        private readonly Channel<ClientFileReadResponse> _channel = Channel.CreateBounded<ClientFileReadResponse>(Environment.ProcessorCount);
        public ClientFileSyncManager(WebViewIPC.WebViewIPCClient client, Guid clientId, string htmlHostPath, ILogger<ClientFileSyncManager> logger)
        {
            _client = client;
            _logger = logger;
            _clientGuid = clientId.ToString();
            _htmlHostPath = htmlHostPath;

            // Ensure the client's cache directory exists
            Directory.CreateDirectory(_clientCacheDirectory);

            // Initiate the duplex streaming call
            _call = _client.RequestClientFileRead();
           
        }

        /// <summary>
        /// Starts handling requests from the server.
        /// </summary>
        public async Task HandleServerRequestsAsync(CancellationToken ct)
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
                    await Parallel.ForEachAsync(_call.ResponseStream.ReadAllAsync(ct),
                       new ParallelOptions { CancellationToken = ct },
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
                                break;
                        }
                    });
                }
                catch (Exception) { }
            });
            await SendInitResponse(_clientGuid, _htmlHostPath);
            _logger.LogInformation($"Sent Init response to server with clientGuid: {_clientGuid}");






        }

        private async Task HandleMetaDataRequestAsync(ServerFileReadRequest request)
        {
            var requestId = request.RequestId;
            var relativeFilePath = request.Path;

            _logger.LogInformation($"Received MetaData request (requestId: {requestId}) for file: {relativeFilePath}");

            // Map the relative file path to the client's cache directory
            string localFilePath = Path.Combine(_clientCacheDirectory, relativeFilePath);

            // Retrieve file metadata
            FileMetadata metadata = GetFileMetadata(localFilePath);
            
            // Create and send the metadata response
            var response = new ClientFileReadResponse
            {
                ClientId = _clientGuid,
                RequestId = requestId,
                Path = relativeFilePath,
                Metadata = metadata
            };
            await _channel.Writer.WriteAsync(response).ConfigureAwait(false);
          
            _logger.LogInformation($"Sent metadata for file: {relativeFilePath}, requestId: {requestId}");
        }

        private async Task HandleFileDataRequestAsync(ServerFileReadRequest request)
        {
            var requestId = request.RequestId;
            var relativeFilePath = request.Path;
            _logger.LogInformation($"Received FileData request (requestId: {requestId}) for file: {relativeFilePath}");

            string localFilePath = Path.Combine(_clientCacheDirectory, relativeFilePath);

            const int chunkSize = 8192; // 8 KB
            byte[] buffer = ArrayPool<byte>.Shared.Rent(chunkSize);

            try
            {
                using var fileStream = new FileStream(localFilePath, FileMode.Open, FileAccess.Read);

                await SendStatusCode(requestId, relativeFilePath, HttpStatusCode.OK);
                // Send FileData messages
                while (true)
                {
                    int bytesRead = await fileStream.ReadAsync(buffer, 0, chunkSize);
                    if (bytesRead == 0) break; // End of file

                    var response = new ClientFileReadResponse
                    {
                        ClientId = _clientGuid,
                        RequestId = requestId,
                        Path = relativeFilePath,
                        FileData = new FileData
                        {
                            FileChunk = ByteString.CopyFrom(buffer, 0, bytesRead),
                        }
                    };
                    await _channel.Writer.WriteAsync(response).ConfigureAwait(false);
                   
                    _logger.LogInformation($"Sent file chunk of size {bytesRead} bytes for file: {relativeFilePath}, requestId: {requestId}");
                }

                await SendCompletionResponse(requestId, relativeFilePath);
            }
            catch (FileNotFoundException)
            {
                _logger.LogWarning($"File '{relativeFilePath}' not found in client's cache.");
                await SendStatusCode(requestId, relativeFilePath, HttpStatusCode.NotFound);
            }
            catch (UnauthorizedAccessException)
            {
                _logger.LogError($"Access denied to file '{relativeFilePath}'.");
                await SendStatusCode(requestId, relativeFilePath, HttpStatusCode.Forbidden);
            }
            catch (IOException ex)
            {
                _logger.LogError(ex, $"IO error reading file '{relativeFilePath}'.");
                await SendStatusCode(requestId, relativeFilePath, HttpStatusCode.InternalServerError);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Unexpected error reading file '{relativeFilePath}'.");
                await SendStatusCode(requestId, relativeFilePath, HttpStatusCode.InternalServerError);
            }
            finally
            {
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
                var fileInfo = new FileInfo(localFilePath);
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
                    LastModified = new DateTimeOffset(fileInfo.LastWriteTimeUtc).ToUnixTimeSeconds(),
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
