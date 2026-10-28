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
        private readonly IClientStreamWriter<ClientFileReadResponse> _requestStream;
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
            _requestStream = _call.RequestStream;
        }

        // Test seam. Production construction opens the duplex call above.
        internal ClientFileSyncManager(string clientId, IFileProvider fileProvider, IClientStreamWriter<ClientFileReadResponse> requestStream, ILogger logger)
        {
            _client = null!;
            _logger = logger;
            _clientGuid = clientId;
            _htmlHostPath = string.Empty;
            _call = null!;
            _requestStream = requestStream;
            _fileProvider = fileProvider;
            _errorCallback = static _ => { };
        }

        internal Task HandleFileDataRequestForTests(ServerFileReadRequest request) => HandleFileDataRequestAsync(request);

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
                    if (_logger.IsEnabled(LogLevel.Debug))
                    {
                        _logger.LogDebug("Sent Init response to server with clientGuid: {ClientGuid}", _clientGuid);
                    }

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
                                _logger.LogWarning("Received unknown request type: {RequestType}", request.RequestType);
                                throw new Exception($"Received unknown request type: {request.RequestType}");
                        }
                    };
                }
                catch (Exception ex) 
                {
                    _errorCallback?.Invoke(ex);
                }
            }, ct);
        }

        private static readonly string WwwRootPrefix = "wwwroot/";
        private static readonly int WwwRootPrefixLength = WwwRootPrefix.Length;

        public static string GetSubPath(string path)
        {
            var normalized = (path ?? string.Empty).Replace('\\', '/').TrimStart('/');
            while (normalized.StartsWith(WwwRootPrefix, StringComparison.OrdinalIgnoreCase))
                normalized = normalized[WwwRootPrefixLength..].TrimStart('/');
            return string.Equals(normalized, "wwwroot", StringComparison.OrdinalIgnoreCase) ? string.Empty : normalized;
        }





        private async Task HandleMetaDataRequestAsync(ServerFileReadRequest request)
        {
            var requestId = request.RequestId;
        
            var subPath = GetSubPath(request.Path);

            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug("Received MetaData request (requestId: {RequestId}) for file: {SubPath}", requestId, subPath);
            }

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
            await _requestStream.WriteAsync(response).ConfigureAwait(false);
          
            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug("Sent metadata for file: {SubPath}, requestId: {RequestId}", subPath, requestId);
            }
        }

        private async Task HandleFileDataRequestAsync(ServerFileReadRequest request)
        {
            var requestId = request.RequestId;
            var subPath = GetSubPath(request.Path);

            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug("Received FileData request (requestId: {RequestId}) for file: {SubPath}", requestId, subPath);
            }

            const int chunkSize = 8192; // 8 KB
            byte[] buffer = ArrayPool<byte>.Shared.Rent(chunkSize);

            try
            {
                var fileInfo = _fileProvider.GetFileInfo(subPath);
                if (!fileInfo.Exists || fileInfo.IsDirectory)
                {
                    await SendCompletionAfterFailure(requestId, subPath).ConfigureAwait(false);
                    return;
                }
                using var fileStream = fileInfo.CreateReadStream();

                // Send FileData messages
                while (true)
                {
                    int bytesRead = await fileStream.ReadAsync(buffer.AsMemory(0, chunkSize));
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
                    await _requestStream.WriteAsync(response).ConfigureAwait(false);
                }

                await SendCompletionResponse(requestId, subPath);
            }
            catch (FileNotFoundException)
            {
                if (_logger.IsEnabled(LogLevel.Warning))
                {
                    _logger.LogWarning("File '{SubPath}' not found in client's cache.", subPath);
                }
                await SendCompletionAfterFailure(requestId, subPath).ConfigureAwait(false);
            }
            catch (UnauthorizedAccessException)
            {
                if (_logger.IsEnabled(LogLevel.Error))
                {
                    _logger.LogError("Access denied to file '{SubPath}'.", subPath);
                }
                await SendCompletionAfterFailure(requestId, subPath).ConfigureAwait(false);
            }
            catch (IOException ex)
            {
                if (_logger.IsEnabled(LogLevel.Error))
                {
                    _logger.LogError(ex, "IO error reading file '{SubPath}'.", subPath);
                }
                await SendCompletionAfterFailure(requestId, subPath).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                if (_logger.IsEnabled(LogLevel.Error))
                {
                    _logger.LogError(ex, "Unexpected error reading file '{SubPath}'.", subPath);
                }
                await SendCompletionAfterFailure(requestId, subPath).ConfigureAwait(false);
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
            await _requestStream.WriteAsync(completionResponse).ConfigureAwait(false);

            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug("Completed file data transfer for file: {RelativeFilePath}, requestId: {RequestId}", relativeFilePath, requestId);
            }
        }
       
        private async Task SendInitResponse(string clientGuid, string htmlHostPath)
        {
            var initResponse = new ClientFileReadResponse
            {
                ClientId = clientGuid,
                RequestId = Guid.Empty.ToString(),
                Path = string.Empty,
                Init = new Init { HtmlHostPath = htmlHostPath }
            };
            await _requestStream.WriteAsync(initResponse).ConfigureAwait(false);

            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug("Sent Init response to server with clientGuid: {ClientGuid}", _clientGuid);
            }
        }

        private FileMetadata GetFileMetadata(string localFilePath)
        {
            try
            {
                var fileInfo = _fileProvider.GetFileInfo(localFilePath);
               

                if (!fileInfo.Exists || fileInfo.IsDirectory)
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
            catch (Exception)
            {
                return new FileMetadata
                {
                    Length = -1,
                    StatusCode = (int)HttpStatusCode.InternalServerError
                };
            }
        }

        private async Task SendCompletionAfterFailure(string requestId, string relativeFilePath)
        {
            try
            {
                await SendCompletionResponse(requestId, relativeFilePath).ConfigureAwait(false);
            }
            catch (InvalidOperationException)
            {
                // The request stream, or the server pipe it feeds, is already completed.
            }
        }

        /// <summary>
        /// Closes the gRPC call gracefully.
        /// </summary>
        public async Task CloseAsync()
        {
            try
            {
                if (_requestStream != null)
                    await _requestStream.CompleteAsync().ConfigureAwait(false);
            }
            finally
            {
                _call?.Dispose();
            }
            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug("Closed request stream.");
            }
        }
    }
}
