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


