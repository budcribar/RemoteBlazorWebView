﻿using Google.Protobuf;
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
        private static AsyncDuplexStreamingCall<ServerFileReadRequest, ClientFileReadResponse>? _fileReader;
        private static Task? _channelReaderTask;
        private static Task? _channelWriterTask;
        public async static Task ShutdownAsync() {
            try
            {
                await (_fileReader?.RequestStream.CompleteAsync() ?? Task.CompletedTask);
            }
            catch { }

            try
            {
                if (_channelReaderTask != null)
                {
                    await _channelReaderTask;
                }

            }
            catch { }
            try
            {
                if (_channelWriterTask != null)
                {
                    await _channelWriterTask;
                }
            }
            catch { }
            _fileReader?.Dispose();
        }

        public static void AttachFileReader(AsyncDuplexStreamingCall<ServerFileReadRequest, ClientFileReadResponse> fileReader, CancellationToken ct, string id, IFileProvider fileProvider,Action<Exception> onException, ILogger? logger = null)
        {
            _fileReader = fileReader;
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
                    await channel.Writer.WriteAsync(new ServerFileReadRequest { Id = id, RequestType = ServerFileReadRequest.Types.RequestType.Init }).ConfigureAwait(false);

                    // Local function to write empty data
                    async Task WriteEofDataAsync(string path, int instance)
                    {
                        await channel.Writer.WriteAsync(new FileReadRequest
                        {
                            Id = id,
                            Data = new FileReadDataRequest { Path = path, Data = ByteString.Empty, Instance=instance  }
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
                                await channel.Writer.WriteAsync(new FileReadRequest { Id = id, Length = new FileReadLengthRequest { Path = message.Path, Length = fileLength,Instance = message.Instance, LastModified=fileInfo.LastModified.ToUnixTimeSeconds() } }).ConfigureAwait(false);

                                if (fileLength == 0)
                                {
                                    await WriteEofDataAsync(message.Path,message.Instance).ConfigureAwait(false); 
                                    return;
                                }

                                // Read file and send data
                                using var stream = fileInfo.CreateReadStream();
                                if (stream == null)
                                {
                                    await WriteEofDataAsync(message.Path,message.Instance).ConfigureAwait(false); 
                                    return;
                                }

                                var buffer = new byte[32 * 1024];
                                int bytesRead;
                                while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, token).ConfigureAwait(false)) > 0)
                                {
                                    var bs = ByteString.CopyFrom(buffer, 0, bytesRead);
                                    await channel.Writer.WriteAsync(new FileReadRequest { Id = id, Data = new FileReadDataRequest { Path = message.Path, Data = bs, Instance=message.Instance } }).ConfigureAwait(false);
                                }

                                // Indicate end of file read
                                await WriteEofDataAsync(message.Path, message.Instance).ConfigureAwait(false); 
                            }
                            catch (FileNotFoundException)
                            {
                                logger?.LogWarning($"File not found {path}");
                                await WriteEofDataAsync(message.Path, message.Instance).ConfigureAwait(false); 
                            }
                            catch (Exception ex)
                            {
                                logger?.LogError(ex, "File reader threw exception");                             
                                await WriteEofDataAsync(message.Path, message.Instance).ConfigureAwait(false);
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
