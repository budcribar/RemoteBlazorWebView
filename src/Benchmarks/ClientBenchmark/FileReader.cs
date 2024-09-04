using Google.Protobuf;
using Microsoft.Extensions.FileProviders;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Channels;
using System.Threading.Tasks;
using PeakSWC.RemoteWebView;
using Grpc.Core;

namespace ClientBenchmark
{
    public static class FileReader
    {

        public static void AttachFileReader(AsyncDuplexStreamingCall<FileReadRequest, FileReadResponse> fileReader, CancellationTokenSource cts, string id, IFileProvider fileProvider)
        {
            _ = Task.Factory.StartNew(async () =>
            {
                try
                {
                    // Initiate the file read request
                    await fileReader.RequestStream.WriteAsync(new FileReadRequest { Id = id, Init = new() });

                    // Process each incoming message concurrently
                    await foreach (var message in fileReader.ResponseStream.ReadAllAsync(cts.Token))
                    {
                        var path = message.Path[(message.Path.IndexOf('/') + 1)..];

                        try
                        {
                            var fileInfo = fileProvider.GetFileInfo(path);
                            var fileLength = fileInfo.Length;
                            await fileReader.RequestStream.WriteAsync(new FileReadRequest { Id = id, Length = new FileReadLengthRequest { Path = message.Path, Length = fileLength } });

                            if (fileLength == 0)
                            {
                                await fileReader.RequestStream.WriteAsync(new FileReadRequest { Id = id, Data = new FileReadDataRequest { Path = message.Path, Data = ByteString.Empty } });
                                continue;
                            }

                            // Read file and send data
                            using var stream = fileInfo.CreateReadStream();
                            if (stream == null)
                            {
                                await fileReader.RequestStream.WriteAsync(new FileReadRequest { Id = id, Data = new FileReadDataRequest { Path = message.Path, Data = ByteString.Empty } });
                                continue;
                            }

                            var buffer = new byte[32 * 1024];  // Increased buffer size to 32KB
                            int bytesRead;
                            while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                            {
                                var bs = ByteString.CopyFrom(buffer, 0, bytesRead);
                                await fileReader.RequestStream.WriteAsync(new FileReadRequest { Id = id, Data = new FileReadDataRequest { Path = message.Path, Data = bs } });
                            }

                            // Indicate end of file read
                            await fileReader.RequestStream.WriteAsync(new FileReadRequest { Id = id, Data = new FileReadDataRequest { Path = message.Path, Data = ByteString.Empty } });
                        }
                        catch (FileNotFoundException)
                        {
                            Console.WriteLine($"File not found: {path}");
                            await fileReader.RequestStream.WriteAsync(new FileReadRequest { Id = id, Data = new FileReadDataRequest { Path = message.Path, Data = ByteString.Empty } });
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine(ex.ToString());
                            await fileReader.RequestStream.WriteAsync(new FileReadRequest { Id = id, Data = new FileReadDataRequest { Path = message.Path, Data = ByteString.Empty } });
                        }
                    }
                    Console.WriteLine("Done reading files");
                }
                catch (Exception ex)
                {
                    //Console.WriteLine(ex.ToString());
                }

            }, cts.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }
        public static void AttachFileReader2(AsyncDuplexStreamingCall<FileReadRequest,FileReadResponse> fileReader, CancellationTokenSource cts, string id, IFileProvider fileProvider)
        {
            // Create a channel with a bounded capacity (adjust as needed)
            //var channel = Channel.CreateUnbounded<FileReadRequest>();
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
                    async Task WriteEmptyDataAsync(string path)
                    {
                        await channel.Writer.WriteAsync(new FileReadRequest
                        {
                            Id = id,
                            Data = new FileReadDataRequest { Path = path, Data = ByteString.Empty }
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
                                await channel.Writer.WriteAsync(new FileReadRequest { Id = id, Length = new FileReadLengthRequest { Path = message.Path, Length = fileLength } });

                                if (fileLength == 0)
                                {
                                    await WriteEmptyDataAsync(message.Path); // Use local function
                                    return;
                                }

                                // Read file and send data
                                using var stream = fileInfo.CreateReadStream();
                                if (stream == null)
                                {
                                    await WriteEmptyDataAsync(message.Path); // Use local function
                                    return;
                                }

                                var buffer = new byte[32 * 1024];
                                int bytesRead;
                                while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, token)) > 0)
                                {
                                    var bs = ByteString.CopyFrom(buffer, 0, bytesRead);
                                    await channel.Writer.WriteAsync(new FileReadRequest { Id = id, Data = new FileReadDataRequest { Path = message.Path, Data = bs } });
                                }

                                // Indicate end of file read
                                await WriteEmptyDataAsync(message.Path); // Use local function
                            }
                            catch (FileNotFoundException)
                            {
                                Console.WriteLine($"File not found: {path}");
                                await WriteEmptyDataAsync(message.Path); // Use local function
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine(ex.ToString());
                                await WriteEmptyDataAsync(message.Path); // Use local function
                            }
                        });

                    // Signal completion to the writer task
                    channel.Writer.Complete();
                    Console.WriteLine("Done reading files");
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.ToString());
                    channel.Writer.Complete(); // Ensure completion in case of error
                }
            }, cts.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }
    }
}
