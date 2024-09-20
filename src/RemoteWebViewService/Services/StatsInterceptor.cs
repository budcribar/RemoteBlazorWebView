using Grpc.Core.Interceptors;
using Grpc.Core;
using System.Threading.Tasks;
using System;
using Google.Protobuf;
using System.Threading;
using static PeakSWC.RemoteWebView.ServerStats;

namespace PeakSWC.RemoteWebView.Services
{
    public class StatsInterceptor : Interceptor
    {
        private readonly ServerStats _stats;

        public StatsInterceptor(ServerStats stats)
        {
            _stats = stats;
        }

        // Override for Unary Calls
        public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
            TRequest request,
            ServerCallContext context,
            UnaryServerMethod<TRequest, TResponse> continuation)
            where TRequest : class, IMessage<TRequest>
            where TResponse : class, IMessage<TResponse>
        {
            _stats.RecordConnectionStart();
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            bool success = false;
            string? errorType = null;
            long bytesReceived = 0;
            long bytesSent = 0;

            try
            {
                // Calculate bytes received (size of request)
                bytesReceived = _stats.CalculateMessageSize(request);
                _stats.RecordBytesReceived(bytesReceived);

                var response = await continuation(request, context);

                // Calculate bytes sent (size of response)
                bytesSent = _stats.CalculateMessageSize(response);
                _stats.RecordBytesSent(bytesSent);

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
                _stats.RecordRequest(success, stopwatch.Elapsed.TotalMilliseconds, errorType);
                _stats.RecordConnectionEnd();
            }
        }

        // Override for Server Streaming Calls
        public override async Task ServerStreamingServerHandler<TRequest, TResponse>(
            TRequest request,
            IServerStreamWriter<TResponse> responseStream,
            ServerCallContext context,
            ServerStreamingServerMethod<TRequest, TResponse> continuation)
            where TRequest : class, IMessage<TRequest>
            where TResponse : class, IMessage<TResponse>
        {
            _stats.RecordConnectionStart();
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            bool success = false;
            string? errorType = null;
            long bytesReceived = 0;
            var bytesSentCounter = new ByteCounter();

            try
            {
                // Calculate bytes received (size of request)
                bytesReceived = _stats.CalculateMessageSize(request);
                _stats.RecordBytesReceived(bytesReceived);

                // Wrap the responseStream to intercept sent messages
                var wrappedStream = new StatsServerStreamWriter<TResponse>(responseStream, _stats, bytesSentCounter);

                await continuation(request, wrappedStream, context);

                // After streaming completes, record bytes sent
                _stats.RecordBytesSent(bytesSentCounter.Bytes);

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
                _stats.RecordRequest(success, stopwatch.Elapsed.TotalMilliseconds, errorType);
                _stats.RecordConnectionEnd();
            }
        }

        // Override for Client Streaming Calls
        public override async Task<TResponse> ClientStreamingServerHandler<TRequest, TResponse>(
            IAsyncStreamReader<TRequest> requestStream,
            ServerCallContext context,
            ClientStreamingServerMethod<TRequest, TResponse> continuation)
            where TRequest : class, IMessage<TRequest>
            where TResponse : class, IMessage<TResponse>
        {
            _stats.RecordConnectionStart();
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            bool success = false;
            string? errorType = null;
            var bytesReceivedCounter = new ByteCounter();
            long bytesSent = 0;

            try
            {
                // Wrap the requestStream to intercept received messages
                var wrappedStream = new StatsAsyncStreamReader<TRequest>(requestStream, _stats, bytesReceivedCounter);

                var response = await continuation(wrappedStream, context);

                // Calculate bytes sent (size of response)
                bytesSent = _stats.CalculateMessageSize(response);
                _stats.RecordBytesSent(bytesSent);

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
                _stats.RecordRequest(success, stopwatch.Elapsed.TotalMilliseconds, errorType);
                _stats.RecordConnectionEnd();
            }
        }

        // Override for Duplex Streaming Calls
        public override async Task DuplexStreamingServerHandler<TRequest, TResponse>(
            IAsyncStreamReader<TRequest> requestStream,
            IServerStreamWriter<TResponse> responseStream,
            ServerCallContext context,
            DuplexStreamingServerMethod<TRequest, TResponse> continuation)
            where TRequest : class, IMessage<TRequest>
            where TResponse : class, IMessage<TResponse>
        {
            _stats.RecordConnectionStart();
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            bool success = false;
            string? errorType = null;
            var bytesReceivedCounter = new ByteCounter();
            var bytesSentCounter = new ByteCounter();

            try
            {
                // Wrap the requestStream to intercept received messages
                var wrappedRequestStream = new StatsAsyncStreamReader<TRequest>(requestStream, _stats, bytesReceivedCounter);

                // Wrap the responseStream to intercept sent messages
                var wrappedResponseStream = new StatsServerStreamWriter<TResponse>(responseStream, _stats, bytesSentCounter);

                await continuation(wrappedRequestStream, wrappedResponseStream, context);

                // After streaming completes, record bytes sent
                _stats.RecordBytesSent(bytesSentCounter.Bytes);

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
                _stats.RecordRequest(success, stopwatch.Elapsed.TotalMilliseconds, errorType);
                _stats.RecordConnectionEnd();
            }
        }

        /// <summary>
        /// Wrapper for IServerStreamWriter to intercept sent messages
        /// </summary>
        private class StatsServerStreamWriter<TResponse> : IServerStreamWriter<TResponse> where TResponse : class, IMessage<TResponse>
        {
            private readonly IServerStreamWriter<TResponse> _inner;
            private readonly ServerStats _stats;
            private readonly ByteCounter _bytesSentCounter;

            public StatsServerStreamWriter(IServerStreamWriter<TResponse> inner, ServerStats stats, ByteCounter bytesSentCounter)
            {
                _inner = inner;
                _stats = stats;
                _bytesSentCounter = bytesSentCounter;
            }

            public WriteOptions? WriteOptions
            {
                get => _inner.WriteOptions;
                set => _inner.WriteOptions = value;
            }

            public async Task WriteAsync(TResponse? message)
            {
                if (message != null)
                {
                    // Calculate bytes sent for this message
                    long size = _stats.CalculateMessageSize(message);
                    Interlocked.Add(ref _bytesSentCounter.Bytes, size);
                }

                await _inner.WriteAsync(message);
            }
        }

        /// <summary>
        /// Wrapper for IAsyncStreamReader to intercept received messages
        /// </summary>
        private class StatsAsyncStreamReader<TRequest> : IAsyncStreamReader<TRequest> where TRequest : class, IMessage<TRequest>
        {
            private readonly IAsyncStreamReader<TRequest> _inner;
            private readonly ServerStats _stats;
            private readonly ByteCounter _bytesReceivedCounter;

            public StatsAsyncStreamReader(IAsyncStreamReader<TRequest> inner, ServerStats stats, ByteCounter bytesReceivedCounter)
            {
                _inner = inner;
                _stats = stats;
                _bytesReceivedCounter = bytesReceivedCounter;
            }

            public TRequest Current => _inner.Current;

            public async Task<bool> MoveNext(CancellationToken cancellationToken)
            {
                var result = await _inner.MoveNext(cancellationToken);
                if (result && _inner.Current != null)
                {
                    // Calculate bytes received for this message
                    long size = _stats.CalculateMessageSize(_inner.Current);
                    Interlocked.Add(ref _bytesReceivedCounter.Bytes, size);
                }
                return result;
            }
        }
    }
}