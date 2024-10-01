using Grpc.Core;
using Microsoft.Extensions.Logging;

namespace PeakSWC.RemoteWebView
{
    public class FileSyncServiceImpl : WebViewIPC.WebViewIPCBase
    {
        private readonly ServerFileSyncManager _fileSyncManager;
        private readonly ILogger<FileSyncServiceImpl> _logger;

        public FileSyncServiceImpl(ServerFileSyncManager fileSyncManager, ILogger<FileSyncServiceImpl> logger)
        {
            _fileSyncManager = fileSyncManager;
            _logger = logger;
        }

        public override async Task RequestClientFileRead(IAsyncStreamReader<ClientFileReadResponse> requestStream, IServerStreamWriter<ServerFileReadRequest> responseStream, ServerCallContext context)
        {
            // Handle Init message
            if (!await requestStream.MoveNext())
            {
                _logger.LogWarning("Client disconnected without sending Init message.");
                throw new RpcException(new Status(StatusCode.InvalidArgument, "Init message not received."));
            }

            var initResponse = requestStream.Current;

            if (initResponse.Init == null)
            {
                _logger.LogWarning("First message from client is not Init.");
                throw new RpcException(new Status(StatusCode.InvalidArgument, "First message must be Init."));
            }

            string clientGuid = initResponse.ClientId;
            _logger.LogInformation($"Client '{clientGuid}' connected and initialized.");

            // Register the client
            _fileSyncManager.RegisterClient(clientGuid, initResponse.Init.HtmlHostPath);

            // Associate the response stream with the clientGuid
            _fileSyncManager.AssociateResponseStream(clientGuid, responseStream);

            // Create a linked cancellation token to handle both client cancellation and server cancellation
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken);
            var cancellationToken = linkedCts.Token;

            try
            {
                // Continuously read messages from the client until cancellation
                while (await requestStream.MoveNext(cancellationToken))
                {
                    var response = requestStream.Current;
                    await _fileSyncManager.HandleClientResponse(response);
                }

                _logger.LogInformation($"Client '{clientGuid}' has completed sending messages.");
            }
            catch (RpcException rpcEx) when (rpcEx.StatusCode == StatusCode.Cancelled)
            {
                _logger.LogInformation($"Client '{clientGuid}' disconnected.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error handling client '{clientGuid}' responses.");
                throw new RpcException(new Status(StatusCode.Internal, "Internal server error."));
            }
            finally
            {
                // Clean up when the client disconnects
                _fileSyncManager.RemoveClient(clientGuid);
                _logger.LogInformation($"Cleaned up resources for client '{clientGuid}'.");
            }
        }
    }
}
