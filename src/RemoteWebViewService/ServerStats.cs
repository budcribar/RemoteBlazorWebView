using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System;
using System.Linq;
using Google.Protobuf;

namespace PeakSWC.RemoteWebView
{
    public class ServerStats
    {
        // Basic request metrics
        private long _totalRequests;
        private long _successfulRequests;
        private long _failedRequests;

        // Response time metrics in milliseconds
        private double _totalResponseTime;
        private double _maxResponseTime;
        private double _minResponseTime = double.MaxValue;

        private ConcurrentBag<double> _responseTimes;

        // Error metrics
        private ConcurrentDictionary<string, long> _errorTypes;

        // Active connections
        private long _activeConnections;

        // Bandwidth metrics in bytes
        private long _totalBytesSent;
        private long _totalBytesReceived;

        public ServerStats()
        {
            _responseTimes = new ConcurrentBag<double>();
            _errorTypes = new ConcurrentDictionary<string, long>();
        }

        /// <summary>
        /// Records a request's statistics.
        /// </summary>
        public void RecordRequest(bool success, double responseTime, string? errorType = null)
        {
            Interlocked.Increment(ref _totalRequests);

            if (success)
            {
                Interlocked.Increment(ref _successfulRequests);
            }
            else
            {
                Interlocked.Increment(ref _failedRequests);
                if (!string.IsNullOrEmpty(errorType))
                {
                    _errorTypes.AddOrUpdate(errorType, 1, (key, oldValue) => oldValue + 1);
                }
            }

            // Thread-safe addition for total response time
            Interlocked.Exchange(ref _totalResponseTime, _totalResponseTime + responseTime);

            _responseTimes.Add(responseTime);

            // Update max response time
            double initialMax, computedMax;
            do
            {
                initialMax = _maxResponseTime;
                computedMax = Math.Max(initialMax, responseTime);
            } while (initialMax != Interlocked.CompareExchange(ref _maxResponseTime, computedMax, initialMax));

            // Update min response time
            double initialMin, computedMin;
            do
            {
                initialMin = _minResponseTime;
                computedMin = Math.Min(initialMin, responseTime);
            } while (initialMin != Interlocked.CompareExchange(ref _minResponseTime, computedMin, initialMin));
        }

        /// <summary>
        /// Records the start of a connection.
        /// </summary>
        public void RecordConnectionStart()
        {
            Interlocked.Increment(ref _activeConnections);
        }

        /// <summary>
        /// Records the end of a connection.
        /// </summary>
        public void RecordConnectionEnd()
        {
            Interlocked.Decrement(ref _activeConnections);
        }

        /// <summary>
        /// Records bytes sent to the client.
        /// </summary>
        public void RecordBytesSent(long bytes)
        {
            Interlocked.Add(ref _totalBytesSent, bytes);
        }

        /// <summary>
        /// Records bytes received from the client.
        /// </summary>
        public void RecordBytesReceived(long bytes)
        {
            Interlocked.Add(ref _totalBytesReceived, bytes);
        }

        /// <summary>
        /// Calculates the average response time.
        /// </summary>
        public double GetAverageResponseTime()
        {
            var totalReq = Interlocked.Read(ref _totalRequests);
            return totalReq > 0 ? _totalResponseTime / totalReq : 0.0;
        }

        /// <summary>
        /// Calculates latency percentiles.
        /// </summary>
        public Dictionary<int, double> GetLatencyPercentiles(List<int>? percentiles = null)
        {
            percentiles ??= new List<int> { 50, 90, 99 };
            var sortedTimes = _responseTimes.OrderBy(x => x).ToList();
            var n = sortedTimes.Count;
            var results = new Dictionary<int, double>();

            foreach (var p in percentiles)
            {
                if (n == 0)
                {
                    results[p] = 0.0;
                    continue;
                }

                double rank = (p / 100.0) * (n - 1);
                int lower = (int)Math.Floor(rank);
                int upper = (int)Math.Ceiling(rank);
                double weight = rank - lower;

                if (upper >= n)
                {
                    results[p] = sortedTimes[^1];
                }
                else
                {
                    results[p] = sortedTimes[lower] * (1 - weight) + sortedTimes[upper] * weight;
                }
            }

            return results;
        }

        /// <summary>
        /// Retrieves all current statistics.
        /// </summary>
        public Dictionary<string, object> GetStats()
        {
            return new Dictionary<string, object>
        {
            { "TotalRequests", Interlocked.Read(ref _totalRequests) },
            { "SuccessfulRequests", Interlocked.Read(ref _successfulRequests) },
            { "FailedRequests", Interlocked.Read(ref _failedRequests) },
            { "AverageResponseTime(ms)", GetAverageResponseTime() },
            { "MaxResponseTime(ms)", _maxResponseTime },
            { "MinResponseTime(ms)", _minResponseTime == double.MaxValue ? 0.0 : _minResponseTime },
            { "ErrorTypes", new Dictionary<string, long>(_errorTypes) },
            { "LatencyPercentiles", GetLatencyPercentiles() },
            { "ActiveConnections", Interlocked.Read(ref _activeConnections) },
            { "TotalBytesSent", Interlocked.Read(ref _totalBytesSent) },
            { "TotalBytesReceived", Interlocked.Read(ref _totalBytesReceived) }
        };
        }

        /// <summary>
        /// Resets all statistics to their initial state.
        /// </summary>
        public void ResetStats()
        {
            Interlocked.Exchange(ref _totalRequests, 0);
            Interlocked.Exchange(ref _successfulRequests, 0);
            Interlocked.Exchange(ref _failedRequests, 0);
            Interlocked.Exchange(ref _totalResponseTime, 0.0);
            Interlocked.Exchange(ref _maxResponseTime, 0.0);
            Interlocked.Exchange(ref _minResponseTime, double.MaxValue);
            _errorTypes.Clear();
            _responseTimes = new ConcurrentBag<double>();
            Interlocked.Exchange(ref _totalBytesSent, 0);
            Interlocked.Exchange(ref _totalBytesReceived, 0);
        }

        /// <summary>
        /// Calculates the size of a Protobuf message in bytes.
        /// </summary>
        public long CalculateMessageSize(IMessage message)
        {
            if (message == null) return 0;
            return message.CalculateSize();
        }
        public class ByteCounter
        {
            public long Bytes { get; set; } = 0;
        }
    }
}