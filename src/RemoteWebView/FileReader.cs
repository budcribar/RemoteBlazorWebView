using Google.Protobuf;
using Microsoft.Extensions.FileProviders;
using System;
using System.Threading.Channels;
using System.Threading.Tasks;
using Grpc.Core;
using System.Threading;
using System.IO;
using Microsoft.Extensions.Logging;

namespace PeakSWC.RemoteWebView
{
    public static class FileReader
    {
        public static void AttachFileReader(AsyncDuplexStreamingCall<FileReadRequest,FileReadResponse> fileReader, CancellationTokenSource cts, string id, IFileProvider fileProvider,Action<Exception> onException, ILogger? logger = null)
        {
            var channel = Channel.CreateBounded<FileReadRequest>(Environment.ProcessorCount);
          
            // Start a task to consume from the channel and write to the stream
            _ = Task.Factory.StartNew(async () =>
            {
                await foreach (var request in channel.Reader.ReadAllAsync(cts.Token))
                {
                    await fileReader.RequestStream.WriteAsync(request);
                }
            }, cts.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default);

            _ = Task.Factory.StartNew(async () =>
            {
                try
                {
                    // Initiate the file read request
                    await channel.Writer.WriteAsync(new FileReadRequest { Id = id, Init = new() });

                    // Local function to write empty data
                    async Task WriteEofDataAsync(string path, int instance)
                    {
                        await channel.Writer.WriteAsync(new FileReadRequest
                        {
                            Id = id,
                            Data = new FileReadDataRequest { Path = path, Data = ByteString.Empty, Instance=instance  }
                        });
                    }

                    // Process each incoming message concurrently using Parallel.ForEach
                    await Parallel.ForEachAsync(fileReader.ResponseStream.ReadAllAsync(cts.Token),
                        new ParallelOptions { CancellationToken = cts.Token },
                        async (message, token) =>
                        {
                            var path = message.Path[(message.Path.IndexOf('/') + 1)..];

                            try
                            {
                                var fileInfo = fileProvider.GetFileInfo(path);
                                var fileLength = fileInfo.Length;
                                await channel.Writer.WriteAsync(new FileReadRequest { Id = id, Length = new FileReadLengthRequest { Path = message.Path, Length = fileLength,Instance = message.Instance } });

                                if (fileLength == 0)
                                {
                                    await WriteEofDataAsync(message.Path,message.Instance); 
                                    return;
                                }

                                // Read file and send data
                                using var stream = fileInfo.CreateReadStream();
                                if (stream == null)
                                {
                                    await WriteEofDataAsync(message.Path,message.Instance); 
                                    return;
                                }

                                var buffer = new byte[32 * 1024];
                                int bytesRead;
                                while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, token)) > 0)
                                {
                                    var bs = ByteString.CopyFrom(buffer, 0, bytesRead);
                                    await channel.Writer.WriteAsync(new FileReadRequest { Id = id, Data = new FileReadDataRequest { Path = message.Path, Data = bs, Instance=message.Instance } });
                                }

                                // Indicate end of file read
                                await WriteEofDataAsync(message.Path, message.Instance); 
                            }
                            catch (FileNotFoundException)
                            {
                                logger?.LogWarning($"File not found {path}");
                                await WriteEofDataAsync(message.Path, message.Instance); 
                            }
                            catch (Exception ex)
                            {
                                logger?.LogError(ex, "File reader threw exception");                             
                                await WriteEofDataAsync(message.Path, message.Instance);
                                onException?.Invoke(ex);
                            }
                        });
                }
                catch (Exception ex)
                {
                    onException?.Invoke(ex);                   
                }
                finally
                {
                    channel.Writer.Complete();
                }
            }, cts.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }
    }
}
