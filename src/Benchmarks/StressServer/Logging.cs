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
        public static void SetupEventLog()
        {
            string source = "StressServerApp";
            string logName = "Application";

            if (!EventLog.SourceExists(source))
            {
                EventLog.CreateEventSource(source, logName);
            }

            eventLog = new EventLog(logName)
            {
                Source = source
            };
        }

        public static void LogEvent(string message, EventLogEntryType entryType)
        {
            eventLog?.WriteEntry(message, entryType);
        }

        public static void ClearEventLog()
        {
            string logName = "Application";

            using (EventLog eventLog = new EventLog(logName))
            {
                // Clear the log
                eventLog.Clear();
                Console.WriteLine($"The '{logName}' event log has been cleared successfully.");
            }
        }
       
    }
}
