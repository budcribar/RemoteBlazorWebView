using Grpc.Core;
using PeakSWC.RemoteWebView;
using Microsoft.Extensions.Logging;
using Google.Protobuf;
using System;
using System.IO;
using System.Threading.Tasks;
using System.Buffers;
using System.Threading;
using Microsoft.Extensions.FileProviders;
using System.Net;

namespace PeakSWC.RemoteWebView
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
            
            // Start reading requests from the server
            _ = Task.Run(async () =>
            {
                try
                {
                    await SendInitResponse(_clientGuid, _htmlHostPath);
                    _logger.LogInformation($"Sent Init response to server with clientGuid: {_clientGuid}");

                    await foreach (var request in _call.ResponseStream.ReadAllAsync(ct))                 
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
                    };
                }
                catch (Exception ex) 
                {
                    _errorCallback?.Invoke(ex);
                }
            });
        }

        private async Task HandleMetaDataRequestAsync(ServerFileReadRequest request)
        {
            var requestId = request.RequestId;
            // TODO
            var subPath = request.Path.Replace("wwwroot/", "");

            //var subPath = request.Path[(request.Path.IndexOf('/') + 1)..]; 

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
            await _call.RequestStream.WriteAsync(response).ConfigureAwait(false);
          
            _logger.LogInformation($"Sent metadata for file: {subPath}, requestId: {requestId}");
        }

        private async Task HandleFileDataRequestAsync(ServerFileReadRequest request)
        {
            var requestId = request.RequestId;
            var subPath = request.Path.Replace("wwwroot/", "");
           
            _logger.LogInformation($"Received FileData request (requestId: {requestId}) for file: {subPath}");

            const int chunkSize = 8192; // 8 KB
            byte[] buffer = ArrayPool<byte>.Shared.Rent(chunkSize);

            try
            {
                using var fileStream = _fileProvider.GetFileInfo(subPath).CreateReadStream();

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
                    await _call.RequestStream.WriteAsync(response).ConfigureAwait(false);
                   
                    _logger.LogInformation($"Sent file chunk of size {bytesRead} bytes for file: {subPath}, requestId: {requestId}");
                }

                await SendCompletionResponse(requestId, subPath);
            }
            catch (FileNotFoundException)
            {
                _logger.LogWarning($"File '{subPath}' not found in client's cache.");         
            }
            catch (UnauthorizedAccessException)
            {
                _logger.LogError($"Access denied to file '{subPath}'.");             
            }
            catch (IOException ex)
            {
                _logger.LogError(ex, $"IO error reading file '{subPath}'.");              
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Unexpected error reading file '{subPath}'.");               
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
            await _call.RequestStream.WriteAsync(completionResponse).ConfigureAwait(false);

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
            await _call.RequestStream.WriteAsync(initResponse).ConfigureAwait(false);
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
                        StatusCode = (int)HttpStatusCode.NotFound,
                        ETag = ""
                    };
                }
                var etag = ETagGenerator.GenerateETag(fileInfo);
                return new FileMetadata
                {
                    Length = fileInfo.Length,
                    LastModified = fileInfo.LastModified.ToUnixTimeSeconds(),
                    StatusCode = (int)HttpStatusCode.OK,
                    ETag = etag
                };
            }
            catch (UnauthorizedAccessException)
            {
                return new FileMetadata { Length = -1, StatusCode = (int)HttpStatusCode.Forbidden, ETag = "" };
            }
            catch (Exception ex)
            {
                return new FileMetadata
                {
                    Length = -1,
                    StatusCode = (int)HttpStatusCode.InternalServerError
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
