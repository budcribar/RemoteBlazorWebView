using Grpc.Core;

using Microsoft.Extensions.Logging;
using System.IO;
using System.Threading.Tasks;
using System;
using System.Threading;

namespace PeakSWC.RemoteWebView
{
    public class FileWatcherService : FileWatcherIPC.FileWatcherIPCBase
    {
        private readonly ILogger<FileWatcherService> _logger;

        public FileWatcherService(ILogger<FileWatcherService> logger)
        {
            _logger = logger;
        }

        public override async Task WatchFile(WatchFileRequest request, IServerStreamWriter<WatchFileResponse> responseStream, ServerCallContext context)
        {
            string filePath = request.FilePath;

            _logger.LogInformation($"Client requested to watch file: {filePath}");

            if (!File.Exists(filePath))
            {
                _logger.LogWarning($"File {filePath} does not exist.");
                throw new RpcException(new Status(StatusCode.NotFound, $"File {filePath} does not exist."));
            }

            // Send initial notification
            string runArguments = GetRunArguments();

            await responseStream.WriteAsync(new WatchFileResponse
            {
                Notification = new FileChangedNotification
                {
                    RunArguments = runArguments
                }
            }).ConfigureAwait(false);

            // Stream the initial file
            await StreamFileInChunks(filePath, responseStream, context.CancellationToken).ConfigureAwait(false);

            // Send zero-length chunk to indicate end of initial transfer
            await responseStream.WriteAsync(new WatchFileResponse
            {
                Chunk = new FileChunk
                {
                    Content = Google.Protobuf.ByteString.Empty
                }
            }).ConfigureAwait(false);

            // Start watching for changes
            FileSystemWatcher? watcher = new FileSystemWatcher(Path.GetDirectoryName(filePath), Path.GetFileName(filePath))
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size
            };

            FileSystemEventHandler handler = async (sender, args) =>
            {
                try
                {
                    watcher.EnableRaisingEvents = false;
                    _logger.LogInformation($"Change detected in file: {filePath}");

                    // Wait briefly to ensure the file write is complete
                    await Task.Delay(500, context.CancellationToken).ConfigureAwait(false);

                    // Send notification about the change
                    string updatedRunArguments = GetRunArguments(); // Update if necessary
                    await responseStream.WriteAsync(new WatchFileResponse
                    {
                        Notification = new FileChangedNotification
                        {
                            RunArguments = updatedRunArguments
                        }
                    }).ConfigureAwait(false);

                    // Stream the updated file
                    await StreamFileInChunks(filePath, responseStream, context.CancellationToken).ConfigureAwait(false);

                    // Send zero-length chunk to indicate end of transfer
                    await responseStream.WriteAsync(new WatchFileResponse
                    {
                        Chunk = new FileChunk
                        {
                            Content = Google.Protobuf.ByteString.Empty
                        }
                    }).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Error processing file change for {filePath}");
                }

                if (watcher != null)
                    watcher.EnableRaisingEvents = true;

            };
            watcher.Error += (sender, args) =>
            {
                _logger.LogError("FileSystemWatcher encountered an error.");
                // Optionally, notify the client about the error
            };
            watcher.Changed += handler;
            watcher.EnableRaisingEvents = true;

            _logger.LogInformation($"Started watching file: {filePath}");

            // Keep the streaming RPC alive until the client disconnects
            try
            {
                await Task.Delay(Timeout.Infinite, context.CancellationToken).ConfigureAwait(false);
            }
            catch (TaskCanceledException)
            {
                _logger.LogInformation($"Client disconnected from watching file: {filePath}");
            }
            finally
            {
                watcher.EnableRaisingEvents = false;
                watcher.Changed -= handler;
                var temp = watcher;
                watcher = null;
                temp.Dispose();
            }
        }

        private async Task StreamFileInChunks(string filePath, IServerStreamWriter<WatchFileResponse> responseStream, CancellationToken cancellationToken)
        {
            const int chunkSize = 32 * 1024; // 32KB

            _logger.LogInformation($"Streaming file: {filePath} in {chunkSize / 1024}KB chunks.");

            using System.IO.FileStream fs = new System.IO.FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

            byte[] buffer = new byte[chunkSize];
            int bytesRead;

            while ((bytesRead = await fs.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false)) > 0)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    _logger.LogInformation("Streaming cancelled.");
                    break;
                }

                byte[] actualBytes = buffer;
                if (bytesRead < chunkSize)
                {
                    actualBytes = new byte[bytesRead];
                    Array.Copy(buffer, actualBytes, bytesRead);
                }

                await responseStream.WriteAsync(new WatchFileResponse
                {
                    Chunk = new FileChunk
                    {
                        Content = Google.Protobuf.ByteString.CopyFrom(actualBytes)
                    }
                }).ConfigureAwait(false);
            }

            _logger.LogInformation($"Completed streaming file: {filePath}");
        }

        private string GetRunArguments()
        {
            // Retrieve run arguments from environment variables or configuration
            string envArgs = Environment.GetEnvironmentVariable("RUN_ARGS");
            if (!string.IsNullOrEmpty(envArgs))
            {
                return envArgs;
            }

            // Default run arguments if none are provided
            return "";
        }
    }
}
