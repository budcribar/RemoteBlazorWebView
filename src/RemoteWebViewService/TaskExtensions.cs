using System.Threading.Tasks;
using System;

namespace PeakSWC.RemoteWebView
{
    public static class TaskExtensions
    {
        public static async Task<T> WaitWithTimeout<T>(this Task<T> task, TimeSpan timeout)
        {
            var completedTask = await Task.WhenAny(task, Task.Delay(timeout)).ConfigureAwait(false);

            if (completedTask == task)
            {
                // Successfully got the result
                return await task.ConfigureAwait(false);
            }
            else
            {
                throw new TimeoutException("Timeout while waiting for the task to complete.");
            }
        }
    }
}
