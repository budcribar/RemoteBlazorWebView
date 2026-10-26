using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

namespace PeakSWC.RemoteWebView
{
    // Temporary ordering log for the refresh shutdown race. Removed once the race is isolated.
    public static class RaceLog
    {
        public static void Write(string message)
        {
            string line = DateTime.UtcNow.ToString("O")
                + " pid=" + Environment.ProcessId
                + " tid=" + Environment.CurrentManagedThreadId
                + " proc=" + SafeProcessName()
                + " " + message;

            try
            {
                string dir = Path.Combine(Path.GetTempPath(), "rbw-race");
                Directory.CreateDirectory(dir);
                string text = line + Environment.NewLine;
                string shared = Path.Combine(dir, "race.log");
                string own = Path.Combine(dir, "race-" + Environment.ProcessId + ".log");

                using var mutex = new Mutex(false, "Local\\RbwRaceLog");
                bool locked = false;
                try
                {
                    try
                    {
                        locked = mutex.WaitOne(TimeSpan.FromSeconds(2));
                    }
                    catch (AbandonedMutexException)
                    {
                        locked = true;
                    }

                    if (locked)
                        File.AppendAllText(shared, text, Encoding.UTF8);
                }
                finally
                {
                    if (locked)
                        mutex.ReleaseMutex();
                }

                File.AppendAllText(own, text, Encoding.UTF8);
            }
            catch
            {
            }

            try
            {
                Console.Error.WriteLine("[race] " + line);
            }
            catch
            {
            }
        }

        private static string SafeProcessName()
        {
            try
            {
                return Process.GetCurrentProcess().ProcessName;
            }
            catch
            {
                return "?";
            }
        }
    }
}
