using Grpc.Core;
using FileWatcher;
using System.IO;
using System.Threading.Tasks;
using System.Threading;
using System;
using static FileWatcher.FileWatcherService;

namespace FileWatcherServerService.Services
{
    public class FileWatcherService : /*FileWatcherService.*/FileWatcherServiceBase
    {
        public override async Task WatchFile(WatchFileRequest request, IServerStreamWriter<FileChangedNotification> responseStream, ServerCallContext context)
        {
            string filePath = request.FilePath;

            if (!File.Exists(filePath))
            {
                throw new RpcException(new Status(StatusCode.NotFound, $"File {filePath} does not exist."));
            }

            // Initial read and send
            byte[] initialContent = await File.ReadAllBytesAsync(filePath);
            string runArguments = GetRunArguments();

            await responseStream.WriteAsync(new FileChangedNotification
            {
                FileContent = Google.Protobuf.ByteString.CopyFrom(initialContent),
                RunArguments = runArguments
            });

            using var watcher = new FileSystemWatcher(Path.GetDirectoryName(filePath), Path.GetFileName(filePath))
            {
                NotifyFilter = NotifyFilters.LastWrite
            };

            var tcs = new TaskCompletionSource();

            FileSystemEventHandler handler = async (sender, args) =>
            {
                try
                {
                    // Wait briefly to ensure the file write is complete
                    await Task.Delay(500);

                    byte[] updatedContent = await File.ReadAllBytesAsync(filePath);
                    string updatedRunArguments = GetRunArguments();

                    await responseStream.WriteAsync(new FileChangedNotification
                    {
                        FileContent = Google.Protobuf.ByteString.CopyFrom(updatedContent),
                        RunArguments = updatedRunArguments
                    });
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error sending file update: {ex.Message}");
                }
            };

            watcher.Changed += handler;
            watcher.EnableRaisingEvents = true;

            try
            {
                await tcs.Task; // Keep the method alive until the client disconnects
            }
            catch (Exception)
            {
                // Handle exceptions if necessary
            }
            finally
            {
                watcher.Changed -= handler;
            }
        }

        private string GetRunArguments()
        {
            // First, try to get from environment variable
            string envArgs = Environment.GetEnvironmentVariable("RUN_ARGS");
            if (!string.IsNullOrEmpty(envArgs))
            {
                return envArgs;
            }

            // If not set, return a default or empty string
            return "default_argument";
        }
    }
}
