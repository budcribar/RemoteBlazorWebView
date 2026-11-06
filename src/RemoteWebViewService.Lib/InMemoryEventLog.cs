using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace PeakSWC.RemoteWebView
{
    public static class InMemoryEventLog
    {
        private const int MaxEntries = 500;
        private static readonly ConcurrentQueue<(DateTime TimestampUtc, List<string> Messages)> Entries = new();

        public static ILoggerProvider Provider { get; } = new InMemoryLoggerProvider();

        static InMemoryEventLog()
        {
            Record(DateTime.UtcNow, [
                "Category: PeakSWC.RemoteWebView.Program [Information]",
                $"RemoteWebViewService started on {RuntimeInformation.OSDescription} ({RuntimeInformation.ProcessArchitecture})"
            ]);
        }

        public static void Record(DateTime timestampUtc, IEnumerable<string> messages)
        {
            var list = messages.Where(m => !string.IsNullOrWhiteSpace(m)).ToList();
            if (list.Count == 0)
                return;

            Entries.Enqueue((DateTime.SpecifyKind(timestampUtc, DateTimeKind.Utc), list));
            while (Entries.Count > MaxEntries)
            {
                if (!Entries.TryDequeue(out _))
                    break;
            }
        }

        public static List<EventResponse> GetEventResponses()
        {
            return Entries
                .OrderByDescending(e => e.TimestampUtc)
                .Select(e =>
                {
                    var er = new EventResponse
                    {
                        Timestamp = Timestamp.FromDateTime(e.TimestampUtc)
                    };
                    er.Messages.AddRange(e.Messages);
                    return er;
                })
                .ToList();
        }

        private sealed class InMemoryLoggerProvider : ILoggerProvider
        {
            public ILogger CreateLogger(string categoryName) => new InMemoryLogger(categoryName);
            public void Dispose() { }
        }

        private sealed class InMemoryLogger(string categoryName) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (!IsEnabled(logLevel) || formatter == null)
                    return;

                var lines = new List<string>
                {
                    $"Category: {categoryName} [{logLevel}]"
                };

                var formatted = formatter(state, exception);
                if (!string.IsNullOrWhiteSpace(formatted))
                {
                    lines.AddRange(formatted.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries));
                }

                if (exception != null)
                {
                    lines.AddRange(exception.ToString().Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries));
                }

                Record(DateTime.UtcNow, lines);
            }
        }
    }
}
