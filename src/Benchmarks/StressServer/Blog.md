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



