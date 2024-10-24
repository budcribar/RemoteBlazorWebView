Server up for 19 hours 576 handles and 31 threads Working Set 187Mb Peak Working Set 217Mb


RemoteWebViewAdmin crashed
9/17/2024 1:59:08
Error when executing service method 'GetClients'.

Exception: 
System.OperationCanceledException: The operation was canceled.
   at System.Threading.Channels.AsyncOperation`1.GetResult(Int16) + 0xe9

9/17/2024 1:59:08 AM

Error when executing service method 'GetClients'.

Exception: 
System.OperationCanceledException: The operation was canceled.
at System.Threading.Channels.AsyncOperation`1.GetResult(Int16) + 0xe9
   at System.Threading.Tasks.ValueTask`1.get_Result() + 0x9d


9/16/2024 10:44:04 PM
RequestPath: /webview.WebViewIPC/Ping

Shutting down 83e60fc3-eb42-483a-93ea-ac4ebbc7eb9a Exception:The request stream was aborted.

9/16/2024 8:23:26 PM
RequestPath: /webview.BrowserIPC/SendMessage

Error when executing service method 'SendMessage'.

Exception: 
System.IO.IOException: The client reset the request stream.
   at Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Http.HttpRequestPipeReader.ValidateState(CancellationToken) + 0x91
   at Microsoft.AspNetCore.Server.Kestrel.Core.Internal.Http.HttpRequestPipeReader.ReadAsync(CancellationToken) + 0x3a


Server starts at 44Mb Working set 475 handles and 30 threads

Category: PeakSWC.RemoteWebView.RemoteWebViewService
EventId: 0
SpanId: 026e640d5780a04b
TraceId: 3987a717b61a3e333b638067df50d2f3
ParentId: 0000000000000000
ConnectionId: 0HN6N1S9LDHGT
RequestId: 0HN6N1S9LDHGT:00000003
RequestPath: /webview.WebViewIPC/FileReader

Shutting down 7bd54828-ac8b-481e-a5e5-0fc3b9f35999 Exception:The request stream was aborted.

Category: PeakSWC.RemoteWebView.RemoteWebViewService
EventId: 0
SpanId: 10a23683120a31d4
TraceId: 7769846d1e59e7c4973a5e8c7a36412b
ParentId: 0000000000000000
ConnectionId: 0HN6N1S9LDHGV
RequestId: 0HN6N1S9LDHGV:00000003
RequestPath: /webview.WebViewIPC/FileReader

Shutting down c289c3d7-012c-486c-9413-b16c0491484a Exception:The request stream was aborted.

Category: PeakSWC.RemoteWebView.RemoteWebViewService
EventId: 0
SpanId: 40490d362ec76671
TraceId: ac3d3cc1336ce41c9a0f5b5fa31c7654
ParentId: 0000000000000000
ConnectionId: 0HN6N1S9LDHG5
RequestId: 0HN6N1S9LDHG5:00000005
RequestPath: /webview.WebViewIPC/Ping

Shutting down 4a6b4e79-c7a9-4a05-a997-dd9a062e28d0 Exception:The request stream was aborted.

Category: PeakSWC.RemoteWebView.RemoteWebViewService
EventId: 0
SpanId: 8684f4ab5f69f185
TraceId: 6c7ca154872c34a9e62485c675a02d2e
ParentId: 0000000000000000
ConnectionId: 0HN6N1S9LDHG5
RequestId: 0HN6N1S9LDHG5:00000003
RequestPath: /webview.WebViewIPC/FileReader

Shutting down 4a6b4e79-c7a9-4a05-a997-dd9a062e28d0 Exception:The request stream was aborted.


Category: Grpc.AspNetCore.Server.ServerCallHandler
EventId: 6
SpanId: f1e055641afb750c
TraceId: 17656ca2769f4c89df57934f8caf49c9
ParentId: 0000000000000000
ConnectionId: 0HN6N1S9LDEP7
RequestId: 0HN6N1S9LDEP7:00000003
RequestPath: /webview.ClientIPC/GetClients

Error when executing service method 'GetClients'.

Exception: 
System.InvalidOperationException: Can't write the message because the request is complete.
   at Grpc.AspNetCore.Server.Internal.HttpContextStreamWriter`1.<WriteCoreAsync>d__14.MoveNext() + 0x261
--- End of stack trace from previous location ---
   at PeakSWC.RemoteWebView.ClientIPCService.<GetClients>d__6.MoveNext() + 0x6a2
--- End of stack trace from previous location ---
   at PeakSWC.RemoteWebView.ClientIPCService.<GetClients>d__6.MoveNext() + 0x970
--- End of stack trace from previous location ---
   at Grpc.Shared.Server.ServerStreamingServerMethodInvoker`3.<Invoke>d__4.MoveNext() + 0x2c0
--- End of stack trace from previous location ---
   at Grpc.Shared.Server.ServerStreamingServerMethodInvoker`3.<Invoke>d__4.MoveNext() + 0x4b3
--- End of stack trace from previous location ---
   at Grpc.AspNetCore.Server.Internal.CallHandlers.ServerStreamingServerCallHandler`3.<HandleCallAsyncCore>d__2.MoveNext() + 0x487
--- End of stack trace from previous location ---
   at Grpc.AspNetCore.Server.Internal.CallHandlers.ServerCallHandlerBase`3.<<HandleCallAsync>g__AwaitHandleCall|8_0>d.MoveNext() + 0x14d


   9/18/24 Made a lot of changes to the server and also some to the client for performance
   let's see what happens

   Before:
   AMD Ryzen AI 9 - 930 loops
   One or more errors occurred. (The HTTP request to the remote WebDriver server for URL http://localhost:64149/session/c8d2120829b22be84c55e4f3ae6e0c49/url timed out after 60 seconds.)

   Snapdragon
   No records found



   SS6 trys to fix this issue 
   Application: StressServer.exe CoreCLR Version: 9.0.24.43107 .NET Version: 9.0.0-rc.1.24431.7 Description: The process was terminated due to an unhandled exception. Exception Info: System.AggregateException: One or more errors occurred. (Index was out of range. Must be non-negative and less than the size of the collection. (Parameter 'index')) (Index was out of range.

   Omen Result
   SS7: Elapsed Time: 00:44:34.1976816 Seconds per pass: 26.741983918000003  Omen
   SS8: Elapsed Time: 00:24:47.0722065 Seconds per pass: 14.870732215999999  Omen

    Elapsed Time: 00:30:25.9628616 Seconds per pass: 18.259640564999998
   SS9: Elapsed Time: 00:22:26.8034051 Seconds per pass: 13.468043221


   Problem: retry can fail with duplicate ID
   Also getting count off error
   Maybe the network went down


   Changed to dotnet8 and had same problems
   Back to dotnet9 and change power plan to never shut down

   Better results
   Omen         Elapsed Time: 00:24:47.0722065 Seconds per pass: 14.870732215999999     1000 passes  (Note Omen ran solo)
   Snapdragon - Elapsed Time: 00:37:26.4888478 Seconds per pass: 22.464924989           1000 passes
   AMD -        Elapsed Time: 00:26:09.9735507 Seconds per pass: 15.699749922999999     1000 passes
   Intel        Elapsed Time: 00:56:19.3942390 Seconds per pass: 33.794014644            997 passes 3 fails


   Try   timedOut = !await fileEntry.Semaphore.WaitAsync(TimeSpan.FromSeconds(60), serviceState.Token);
   Also power profile set to always on

   Snapdragon   Elapsed Time: 00:43:32.6292145 Seconds per pass: 26.126336556000002     1000 passes
   amd          Elapsed Time: 00:26:23.1169390 Seconds per pass: 15.831183454'          Counter Passes: 990 Fails: 10 ExecuteLoop encountered an exception: The HTTP request to the remote WebDriver server for URL http://localhost:55543/session/995c90a9df1a599b15202749d9e3d37a/url timed out after 60 seconds.  4:37 AM
   Intel        Elapsed Time: 00:55:36.5732819 Seconds per pass: 33.365792256           Counter Passes: 990 Fails: 10 10 expected but found Current count: 5 4:58:19AM
   Maximum response time is 67.395 seconds


    Category: PeakSwc.StaticFiles.RemoteFileResolver EventId: 0 SpanId: 5c19dd60195855b7 TraceId: 2e4c9e6dee2a7e50c3b6726641c1b55e ParentId: 0000000000000000 ConnectionId: 0HN6Q5ATLNOU7 RequestId: 0HN6Q5ATLNOU7:0000004D RequestPath: /d63ab5e5-5d6d-4ba7-98bc-0b3cfa6be45f/_content/RemoteBlazorWebViewTutorial.Shared/css/open-iconic/font/fonts/open-iconic.woff Unable to insert wwwroot/_content/RemoteBlazorWebViewTutorial.Shared/css/open-iconic/font/fonts/open-iconic.woff id d63ab5e5-5d6d-4ba7-98bc-0b3cfa6be45f to dictionary  
 
 This is running Release mode
  Before:
   Max response time 26.7 sec
  ThreadPool.SetMinThreads(workerThreads: 100, completionPortThreads: 100);
  After 
   Max response time 13.7 sec

  ThreadPool.SetMinThreads(workerThreads: 200, completionPortThreads: 200);
  Max response time 13.1 sec

  Now move back to 100 worker and port threads, run production
  With 3 systems at 50 connections a piece we will stick with 200 threads


  4 systems running in production mode
  First, let's see how AMD runs
  13.7 seconds max
  Now,add Omen
  still max 13.7 seconds
  now add Qualcomm -> max 29 seconds
  now add Intel -> max 35.3 seconds
  avg 640ms max 35316ms

  All 4 systems pass!!!
  QualComm Elapsed Time: 00:38:57.9440700 Seconds per pass: 23.379478273999997
  AMD      Elapsed Time: 00:24:15.7761510 Seconds per pass: 14.557772992
  Intel    Elapsed Time: 00:55:14.7821006 Seconds per pass: 33.148158523
  Omen     Elapsed Time: 00:22:31.7757289 Seconds per pass: 13.517767193
  
  
 Using production server now 
  Omen     Elapsed Time: 00:22:11.2683630 Seconds per pass: 13.31269382   1000 passes
  QualComm Elapsed Time: 00:49:17.9974374 Seconds per pass: 29.580037116
 Intel     Elapsed Time: 00:56:57.1143658 Seconds per pass: 34.171262776
 Amd       Elapsed Time: 00:24:29.9418117 Seconds per pass: 14.699428143


 app.css "1dac418b70a0e36"

 Cached:
 24 requests
28.7 kB transferred
441 kB resources
Finish: 1.09 s
DOMContentLoaded: 73 ms
Load: 182 ms

Not cached:
24 requests
176 kB transferred
473 kB resources
Finish: 1.14 s
DOMContentLoaded: 492 ms
Load: 596 ms

Server sends FileReadResponse (Path + Instance)
Client is in loop FileReader.cs
Gets a path then writes out
1. Init
2. Length + LastModified (FileReadLengthRequest)
3. Data FileReadDataRequest

Server is CreateReadStream


Still reading but should 
Omen Aloane Elapsed Time: 00:22:11.2487534 Seconds per pass: 13.312496605 (oops disable cache on chrome)


10/23/24

Getting Timeout waiting for client to register

Getting errors with 10 or 15 clients per loop
5 clients per loop
Omen      Elapsed Time: 00:08:53.0392569 Seconds per pass: 5.3304024960000005   500 passes
QualComm  Elapsed Time: 00:17:43.7368850 Seconds per pass: 10.637413630000001
Intel     Elapsed Time: 00:16:50.6624376 Seconds per pass: 10.106659353
Amd       Elapsed Time: 00:11:44.1810467 Seconds per pass: 7.041826286 


Client and server cache 5 clients per loop
Omen        Elapsed Time: 00:08:45.8324661 Seconds per pass: 5.258333784
Qualcomm    Elapsed Time: 00:17:29.9424616 Seconds per pass: 10.499472235999999
Intel       Elapsed Time: 00:17:02.5863716 Seconds per pass: 10.225888786 
amd         Elapsed Time: 00:11:50.5901785 Seconds per pass: 7.105918794000001