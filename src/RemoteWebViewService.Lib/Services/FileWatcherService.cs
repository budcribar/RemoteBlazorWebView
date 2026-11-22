using Grpc.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PeakSWC.RemoteWebView.Agents;
using System.IO;
using System.Threading.Tasks;
using System;
using System.Threading;

namespace PeakSWC.RemoteWebView
{
    public class FileWatcherService(
        ILogger<FileWatcherService> logger,
        IRelayAuthorization? authorization = null,
        IAgentTenantPolicy? tenantPolicy = null,
        AgentOptions? agentOptions = null,
        IUserService? userService = null,
        IConfiguration? configuration = null) : FileWatcherIPC.FileWatcherIPCBase
    {
        public override async Task WatchFile(WatchFileRequest request, IServerStreamWriter<WatchFileResponse> responseStream, ServerCallContext context)
        {
            if (authorization?.Enabled == true)
            {
                var scope = await RelayCallerScope.ResolveAsync(context, authorization, tenantPolicy, agentOptions, userService).ConfigureAwait(false);
                if (!scope.IsOperator)
                    throw new RpcException(new Status(StatusCode.PermissionDenied, "Only a relay operator may watch files."));
            }

            string filePath = request?.FilePath ?? string.Empty;
            if (string.IsNullOrWhiteSpace(filePath))
            {
                throw new RpcException(new Status(StatusCode.InvalidArgument, "FilePath cannot be null or empty."));
            }

            string fullPath = Path.GetFullPath(filePath);
            string? allowedRoot = configuration?["FileWatcher:AllowedRoot"];
            if (authorization?.Enabled == true && string.IsNullOrWhiteSpace(allowedRoot))
            {
                throw new RpcException(new Status(StatusCode.PermissionDenied, "FileWatcher:AllowedRoot must be configured when sign-in is enabled."));
            }
            if (!string.IsNullOrWhiteSpace(allowedRoot))
            {
                string normalizedRoot = Path.GetFullPath(allowedRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
                var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
                if (!fullPath.StartsWith(normalizedRoot, comparison))
                {
                    logger.LogWarning("Rejected WatchFile request outside allowed root: {FilePath}", filePath);
                    throw new RpcException(new Status(StatusCode.PermissionDenied, "FilePath is outside the allowed root directory."));
                }
            }

            logger.LogInformation($"Client requested to watch file: {fullPath}");

            if (!File.Exists(fullPath))
            {
                logger.LogWarning($"File {fullPath} does not exist.");
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
            }, context.CancellationToken).ConfigureAwait(false);

            // Stream the initial file
            await StreamFileInChunks(fullPath, responseStream, context.CancellationToken).ConfigureAwait(false);

            // Send zero-length chunk to indicate end of initial transfer
            await responseStream.WriteAsync(new WatchFileResponse
            {
                Chunk = new FileChunk
                {
                    Content = Google.Protobuf.ByteString.Empty
                }
            }, context.CancellationToken).ConfigureAwait(false);

            // Start watching for changes
            string watchDir = Path.GetDirectoryName(fullPath) is { Length: > 0 } d ? d : Directory.GetCurrentDirectory();
            FileSystemWatcher? watcher = new(watchDir, Path.GetFileName(fullPath))
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size
            };

            FileSystemEventHandler handler = async (sender, args) =>
            {
                try
                {
                    watcher.EnableRaisingEvents = false;
                    logger.LogInformation($"Change detected in file: {filePath}");

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
                    }, context.CancellationToken).ConfigureAwait(false);

                    // Stream the updated file
                    await StreamFileInChunks(filePath, responseStream, context.CancellationToken).ConfigureAwait(false);

                    // Send zero-length chunk to indicate end of transfer
                    await responseStream.WriteAsync(new WatchFileResponse
                    {
                        Chunk = new FileChunk
                        {
                            Content = Google.Protobuf.ByteString.Empty
                        }
                    }, context.CancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, $"Error processing file change for {filePath}");
                }

                if (watcher != null)
                    watcher.EnableRaisingEvents = true;

            };
            watcher.Error += (sender, args) =>
            {
                logger.LogError("FileSystemWatcher encountered an error.");
                // Optionally, notify the client about the error
            };
            watcher.Changed += handler;
            watcher.EnableRaisingEvents = true;

            logger.LogInformation($"Started watching file: {filePath}");

            // Keep the streaming RPC alive until the client disconnects
            try
            {
                await Task.Delay(Timeout.Infinite, context.CancellationToken).ConfigureAwait(false);
            }
            catch (TaskCanceledException)
            {
                logger.LogInformation($"Client disconnected from watching file: {filePath}");
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

            logger.LogInformation($"Streaming file: {filePath} in {chunkSize / 1024}KB chunks.");

            using System.IO.FileStream fs = new(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

            byte[] buffer = new byte[chunkSize];
            int bytesRead;

            while ((bytesRead = await fs.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false)) > 0)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    logger.LogInformation("Streaming cancelled.");
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
                }, cancellationToken).ConfigureAwait(false);
            }

            logger.LogInformation($"Completed streaming file: {filePath}");
        }

        private string GetRunArguments()
        {
            // Retrieve run arguments from environment variables or configuration
            string envArgs = Environment.GetEnvironmentVariable("RUN_ARGS") ?? string.Empty;
            if (!string.IsNullOrEmpty(envArgs))
            {
                return envArgs;
            }

            // Default run arguments if none are provided
            return "";
        }
    }
}
