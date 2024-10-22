using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace StressServer
{
    internal class Logging
    {
        private static EventLog? eventLog;
        private static readonly string source = "StressServerApp"; // Ensure this matches the pre-created source
        private static readonly string logName = "Application";      // Ensure this matches the pre-created log

        public static void SetupEventLog()
        {
            try
            {
                if (!EventLog.SourceExists(source))
                {
                    EventLog.CreateEventSource(source, logName);
                }
            }
            catch { }

            try
            {
                // Initialize the EventLog instance with the pre-created source and log
                eventLog = new EventLog(logName)
                {
                    Source = source
                };

                // Optionally, verify that the source is correctly associated
                if (eventLog.Source != source)
                {
                    throw new InvalidOperationException($"Event source '{source}' is not associated with log '{logName}'.");
                }
            }
            catch (Exception ex)
            {
                // Handle exceptions gracefully, possibly fallback to another logging mechanism
                Console.WriteLine($"Failed to initialize EventLog: {ex.Message}");
                // Optionally, initialize a fallback logger (e.g., file logger)
            }
        }

        public static void LogEvent(string message, EventLogEntryType entryType)
        {
            try
            {
                if (eventLog == null)
                {
                    // Attempt to set up the event log if not already done
                    SetupEventLog();
                }

                eventLog?.WriteEntry(message, entryType);
            }
            catch (Exception ex)
            {
                // Handle exceptions gracefully, possibly fallback to another logging mechanism
                Console.WriteLine($"Failed to write to EventLog: {ex.Message}");
                // Optionally, log to a file or another logging system
            }
        }

        public static void ClearEventLog()
        {
            try
            {
                if (!EventLog.SourceExists(source))
                {
                    Console.WriteLine($"Event source '{source}' does not exist. Cannot clear log.");
                    return;
                }

                using (EventLog log = new EventLog(logName))
                {
                    // Clear the log
                    log.Clear();
                    Console.WriteLine($"The '{logName}' event log has been cleared successfully.");
                }
            }
            catch (Exception ex)
            {
                // Handle exceptions gracefully
                Console.WriteLine($"Failed to clear EventLog: {ex.Message}");
            }
        }
    }
}
