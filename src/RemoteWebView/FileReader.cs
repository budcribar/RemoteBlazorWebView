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
        public static void AttachFileReader(AsyncDuplexStreamingCall<ClientFileReadResponse, ServerFileReadRequest> fileReader, CancellationToken ct, string id, IFileProvider fileProvider,Action<Exception> onException, ILogger? logger = null)
        {
            var channel = Channel.CreateBounded<ClientFileReadResponse>(Environment.ProcessorCount);
          
            // Start a task to consume from the channel and write to the stream
            _channelReaderTask = Task.Factory.StartNew(async () =>
            {
                _fileReader.RequestStream
                try
                {
                    await foreach (var clientResponse in channel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
                    {
                        await fileReader.RequestStream.WriteAsync(clientResponse).ConfigureAwait(false);
                    }
                }
                catch (Exception ex)
                {
                    onException?.Invoke(ex);
                }    
            }, ct, TaskCreationOptions.LongRunning, TaskScheduler.Default);

            _channelWriterTask = Task.Factory.StartNew(async () =>
            {
                try
                {
                    // Initiate the file read request
                    await channel.Writer.WriteAsync(new ClientFileReadResponse { ClientId = id, Init = new() }).ConfigureAwait(false);

                    // Local function to write empty data
                    async Task WriteEofDataAsync(string path, string instance)
                    {
                        await channel.Writer.WriteAsync(new ClientFileReadResponse
                        {
                            ClientId = id,
                            RequestId = instance,
                            Path = path,
                            FileData = new FileData {FileChunk = ByteString.Empty }
                        }).ConfigureAwait(false);
                    }

                    // Process each incoming message concurrently using Parallel.ForEach
                    await Parallel.ForEachAsync(fileReader.ResponseStream.ReadAllAsync(ct),
                        new ParallelOptions { CancellationToken = ct },
                        async (message, token) =>
                        {
                            var path = message.Path[(message.Path.IndexOf('/') + 1)..];

                            try
                            {
                                var fileInfo = fileProvider.GetFileInfo(path);
                                var fileLength = fileInfo.Length;
                                await channel.Writer.WriteAsync(new 
                                    ClientFileReadResponse { ClientId = id, RequestId = message.RequestId, Path = message.Path, Metadata = new FileMetadata {Length=fileLength }}).ConfigureAwait(false);

                                if (fileLength == 0)
                                {
                                    await WriteEofDataAsync(message.Path,message.RequestId).ConfigureAwait(false); 
                                    return;
                                }

                                // Read file and send data
                                using var stream = fileInfo.CreateReadStream();
                                if (stream == null)
                                {
                                    await WriteEofDataAsync(message.Path,message.RequestId).ConfigureAwait(false); 
                                    return;
                                }

                                var buffer = new byte[32 * 1024];
                                int bytesRead;
                                while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, token).ConfigureAwait(false)) > 0)
                                {
                                    var bs = ByteString.CopyFrom(buffer, 0, bytesRead);
                                    await channel.Writer.WriteAsync(new ClientFileReadResponse { ClientId = id, RequestId = message.RequestId, Path = message.Path, FileData = new FileData {  FileChunk = bs,  } }).ConfigureAwait(false);
                                }

                                // Indicate end of file read
                                await WriteEofDataAsync(message.Path, message.RequestId).ConfigureAwait(false); 
                            }
                            catch (FileNotFoundException)
                            {
                                logger?.LogWarning($"File not found {path}");
                                await WriteEofDataAsync(message.Path, message.RequestId).ConfigureAwait(false); 
                            }
                            catch (Exception ex)
                            {
                                logger?.LogError(ex, "File reader threw exception");                             
                                await WriteEofDataAsync(message.Path, message.RequestId).ConfigureAwait(false);
                                onException?.Invoke(ex);
                            }
                        }).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    onException?.Invoke(ex);                   
                }
                finally
                {
                    channel.Writer.Complete();
                }
            }, ct, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }
    }
}
