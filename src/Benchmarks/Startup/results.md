# RemoteBlazorWebViewServer.exe

## Size:
187,383,398

### dotnet8
| Run  | Result 1 | Result 2 | Result 3 | Result 4 |
|------|----------|----------|----------|----------|
| 1st  | 2247     | 2248     | 2262     | 2251     |
| 2nd  | 2        | 2        | 1        | 2        |
| 3rd  | 2        | 2        | 1        | 2        |
| 4th  | 1        | 0        | 0        | 0        |

## Used AOT

### Size:
31,302,656

### dotnet8
| Run  | Result 1 | Result 2 | Result 3 |
|------|----------|----------|----------|
| 1st  | 2569     | 2205     | 2258     |
| 2nd  | 6        | 5        | 1        |
| 3rd  | 6        | 5        | 1        |
| 4th  | 1        | 1        | 0        |


### dotnet9 Release Mode Size 32,243,712

| Run  | Result 1 | Result 2 | Result 3 |
|------|----------|----------|----------|
| 1st  | 2241ms   |  2260ms  | 2272     |
| 2nd  | 1ms      |     1ms  | 1        |
| 3rd  | 1ms      |     1ms  | 1        |
| 4th  | 0ms      |     1ms  | 0        |
| 500  | 96 ms    |    91ms  | 80ms     |
|      | 24ms     |    24ms  | 24ms     | 100 GetServerStatusAsync
|      | 20ms     |    20ms  | 20ms     | 125 GetServerStatusAsync
|      | 34ms     |    34ms  | 34ms     | 125 CreateWebViewRequest



### dotnet 9 Release Mode (Server is running via Visual Studio in release)
|500 CreateWebViewRequest| 42 | 54 | 54 | 54 | 56
|500 Shutdown request | 61 | 46 | 40 | 43 | 57


### dotnet 9 Release Mode (Server is running via Visual Studio in debug)
|500 CreateWebViewRequest| 47 | 52 | 47 | Timed out after 100 
|500 Shutdown request | 137 | 151 | 121


20 x spawn 10 CreateWebView processes
Hung after loop 11
Server running in Release mode


20 x spawn 10 CreateWebView processes
Finished all 20 loops
Server running in Debug mode
No events in viewer


Reverted back to dotnet8 and server is running flawless !!

20 x spawn 100 CreateWebView processes
Finished all 20 loops
Server running in Debug mode
No events in viewer


20 x spawn 1000 CreateWebView processes
Timing out so moved to 5 seconds
Removed rate limiter
Task.WaitAll(tasks, 10000);
Task.WaitAll(tasks, 20000);
Timing out so moved to 7


System.RuntimeType[]	200 B	1000
System.Collections.Concurrent.ConcurrentQueueSegment+Slot<PeakSWC.RemoteWebView.WebMessageResponse>[]	15.63 KB	1000
System.Collections.Concurrent.ConcurrentQueueSegment+Slot<PeakSWC.RemoteWebView.StringRequest>[]	15.63 KB	1000
System.Collections.Concurrent.ConcurrentDictionary+VolatileNode<PeakSWC.RemoteWebView.BrowserResponseNode, System.Collections.Concurrent.BlockingCollection<PeakSWC.RemoteWebView.StringRequest>>[]	289.06 KB	1000
System.Collections.Concurrent.ConcurrentDictionary+VolatileNode<System.String, PeakSWC.RemoteWebView.FileEntry>[]	289.06 KB	1000
System.Collections.Concurrent.ConcurrentQueueSegment+Slot<System.String>[]	15.63 KB	1000
System.Collections.Concurrent.ConcurrentDictionary+VolatileNode<System.UInt32, PeakSWC.RemoteWebView.SendSequenceMessageRequest>[]	289.06 KB	1000


System.RuntimeType[]	200 B	1000
System.Collections.Concurrent.ConcurrentQueueSegment+Slot<PeakSWC.RemoteWebView.WebMessageResponse>[]	15.63 KB	1000
System.Collections.Concurrent.ConcurrentQueueSegment+Slot<PeakSWC.RemoteWebView.StringRequest>[]	15.63 KB	1000
System.Collections.Concurrent.ConcurrentDictionary+VolatileNode<PeakSWC.RemoteWebView.BrowserResponseNode, System.Collections.Concurrent.BlockingCollection<PeakSWC.RemoteWebView.StringRequest>>[]	289.06 KB	1000
System.Collections.Concurrent.ConcurrentDictionary+VolatileNode<System.String, PeakSWC.RemoteWebView.FileEntry>[]	289.06 KB	1000
System.Collections.Concurrent.ConcurrentQueueSegment+Slot<System.String>[]	15.63 KB	1000
System.Collections.Concurrent.ConcurrentDictionary+VolatileNode<System.UInt32, PeakSWC.RemoteWebView.SendSequenceMessageRequest>[]	289.06 KB	1000



