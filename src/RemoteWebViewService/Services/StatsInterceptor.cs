using Grpc.Core.Interceptors;
using Grpc.Core;
using System.Threading.Tasks;
using System;
using Google.Protobuf;
using System.Threading;
using static PeakSWC.RemoteWebView.ServerStats;
using Microsoft.Extensions.Logging;

namespace PeakSWC.RemoteWebView.Services
{
    public class StatsInterceptor(ServerStats stats) : Interceptor
    {
        private const double ThresholdMs = 50000; // 50 seconds threshold for logging long requests

        // Override for Unary Calls
        public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
            TRequest request, ServerCallContext context, UnaryServerMethod<TRequest, TResponse> continuation)
            where TRequest : class where TResponse : class
        {
            stats.RecordConnectionStart();
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            bool success = false;
            string? errorType = null;
            long bytesReceived = 0;
            long bytesSent = 0;

            try
            {
                if (request is IMessage message)
                {
                    bytesReceived = stats.CalculateMessageSize(message);
                }
                stats.RecordBytesReceived(bytesReceived);

                var response = await continuation(request, context).ConfigureAwait(false);

                if (response is IMessage message2)
                {
                    bytesSent = stats.CalculateMessageSize(message2);
                }
                stats.RecordBytesSent(bytesSent);

                success = true;
                return response;
            }
            catch (RpcException rpcEx)
            {
                errorType = rpcEx.StatusCode.ToString();
                throw;
            }
            catch (Exception ex)
            {
                errorType = ex.GetType().Name;
                throw new RpcException(new Status(StatusCode.Internal, ex.Message));
            }
            finally
            {
                stopwatch.Stop();
                var elapsedTime = stopwatch.Elapsed.TotalMilliseconds;
                stats.RecordRequest(success, elapsedTime, errorType);

                if (elapsedTime > ThresholdMs)
                {
                    //_logger.LogWarning($"Unary request exceeded {ThresholdMs}ms: {context.Method}, Duration: {elapsedTime}ms");
                }

                stats.RecordConnectionEnd();
            }
        }

        // Override for Server Streaming Calls
        public override async Task ServerStreamingServerHandler<TRequest, TResponse>(
            TRequest request,
            IServerStreamWriter<TResponse> responseStream,
            ServerCallContext context,
            ServerStreamingServerMethod<TRequest, TResponse> continuation)
            where TRequest : class
            where TResponse : class
        {
            stats.RecordConnectionStart();
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            bool success = false;
            string? errorType = null;
            long bytesReceived = 0;

            try
            {
                if (request is IMessage message)
                {
                    bytesReceived = stats.CalculateMessageSize(message);
                }
                stats.RecordBytesReceived(bytesReceived);

                var wrappedStream = new StatsServerStreamWriter<TResponse>(responseStream, stats);

                await continuation(request, wrappedStream, context).ConfigureAwait(false);

                stats.RecordBytesSent(wrappedStream.BytesSent);

                success = true;
            }
            catch (RpcException rpcEx)
            {
                errorType = rpcEx.StatusCode.ToString();
                throw;
            }
            catch (Exception ex)
            {
                errorType = ex.GetType().Name;
                throw new RpcException(new Status(StatusCode.Internal, ex.Message));
            }
            finally
            {
                stopwatch.Stop();
                var elapsedTime = stopwatch.Elapsed.TotalMilliseconds;
                stats.RecordRequest(success, elapsedTime, errorType);

                if (elapsedTime > ThresholdMs)
                {
                    //_logger.LogWarning($"Server streaming request exceeded {ThresholdMs}ms: {context.Method}, Duration: {elapsedTime}ms");
                }

                stats.RecordConnectionEnd();
            }
        }

        // Override for Client Streaming Calls
        public override async Task<TResponse> ClientStreamingServerHandler<TRequest, TResponse>(
            IAsyncStreamReader<TRequest> requestStream,
            ServerCallContext context,
            ClientStreamingServerMethod<TRequest, TResponse> continuation)
            where TRequest : class where TResponse : class
        {
            stats.RecordConnectionStart();
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            bool success = false;
            string? errorType = null;
            long bytesSent = 0;

            try
            {
                var wrappedStream = new StatsAsyncStreamReader<TRequest>(requestStream, stats);

                var response = await continuation(wrappedStream, context).ConfigureAwait(false);

                if (response is IMessage message)
                {
                    bytesSent = stats.CalculateMessageSize(message);
                }
                stats.RecordBytesSent(bytesSent);

                success = true;
                return response;
            }
            catch (RpcException rpcEx)
            {
                errorType = rpcEx.StatusCode.ToString();
                throw;
            }
            catch (Exception ex)
            {
                errorType = ex.GetType().Name;
                throw new RpcException(new Status(StatusCode.Internal, ex.Message));
            }
            finally
            {
                stopwatch.Stop();
                var elapsedTime = stopwatch.Elapsed.TotalMilliseconds;
                stats.RecordRequest(success, elapsedTime, errorType);

                if (elapsedTime > ThresholdMs)
                {
                    //_logger.LogWarning($"Client streaming request exceeded {ThresholdMs}ms: {context.Method}, Duration: {elapsedTime}ms");
                }

                stats.RecordConnectionEnd();
            }
        }

        // Override for Duplex Streaming Calls
        public override async Task DuplexStreamingServerHandler<TRequest, TResponse>(
            IAsyncStreamReader<TRequest> requestStream,
            IServerStreamWriter<TResponse> responseStream,
            ServerCallContext context,
            DuplexStreamingServerMethod<TRequest, TResponse> continuation)
            where TRequest : class
            where TResponse : class
        {
            stats.RecordConnectionStart();
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            bool success = false;
            string? errorType = null;

            try
            {
                var wrappedRequestStream = new StatsAsyncStreamReader<TRequest>(requestStream, stats);
                var wrappedResponseStream = new StatsServerStreamWriter<TResponse>(responseStream, stats);

                await continuation(wrappedRequestStream, wrappedResponseStream, context).ConfigureAwait(false);

                stats.RecordBytesSent(wrappedResponseStream.BytesSent);

                success = true;
            }
            catch (RpcException rpcEx)
            {
                errorType = rpcEx.StatusCode.ToString();
                throw;
            }
            catch (Exception ex)
            {
                errorType = ex.GetType().Name;
                throw new RpcException(new Status(StatusCode.Internal, ex.Message));
            }
            finally
            {
                stopwatch.Stop();
                var elapsedTime = stopwatch.Elapsed.TotalMilliseconds;
                stats.RecordRequest(success, elapsedTime, errorType);

                if (elapsedTime > ThresholdMs)
                {
                    //_logger.LogWarning($"Duplex streaming request exceeded {ThresholdMs}ms: {context.Method}, Duration: {elapsedTime}ms");
                }

                stats.RecordConnectionEnd();
            }
        }

        /// <summary>
        /// Wrapper for IServerStreamWriter to intercept sent messages
        /// </summary>
        private class StatsServerStreamWriter<TResponse>(IServerStreamWriter<TResponse> inner, ServerStats stats) : IServerStreamWriter<TResponse>
            where TResponse : class
        {
            private long _bytesSent = 0; // Backing field

            public long BytesSent => Interlocked.Read(ref _bytesSent);

            public WriteOptions? WriteOptions
            {
                get => inner.WriteOptions;
                set => inner.WriteOptions = value;
            }

            public async Task WriteAsync(TResponse message)
            {
                // Calculate bytes sent for this message
                if (message is IMessage message2)
                {
                    long size = stats.CalculateMessageSize(message2);
                    Interlocked.Add(ref _bytesSent, size); // Thread-safe increment
                }

                await inner.WriteAsync(message).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Wrapper for IAsyncStreamReader to intercept received messages
        /// </summary>
        private class StatsAsyncStreamReader<TRequest>(IAsyncStreamReader<TRequest> inner, ServerStats stats) : IAsyncStreamReader<TRequest>
            where TRequest : class
        {
            private long _bytesReceived = 0; // Backing field

            public long BytesReceived => Interlocked.Read(ref _bytesReceived);

            public TRequest Current => inner.Current;

            public async Task<bool> MoveNext(CancellationToken cancellationToken)
            {
                var result = await inner.MoveNext(cancellationToken).ConfigureAwait(false);
                if (result && inner.Current is IMessage message)
                {
                    // Calculate bytes received for this message
                    long size = stats.CalculateMessageSize(message);
                    Interlocked.Add(ref _bytesReceived, size); // Thread-safe increment
                }
                return result;
            }
        }
    }
}

