#nullable disable
using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Microsoft.JSInterop.Infrastructure;

namespace PeakSWC.RemoteBlazorWebView.Windows
{
    internal class WebViewJSRuntime : JSRuntime
    {
        private IpcSender _ipcSender;

        public ElementReferenceContext ElementReferenceContext { get; }

        public WebViewJSRuntime()
        {
            ElementReferenceContext = new WebElementReferenceContext(this);
            JsonSerializerOptions.Converters.Add(
                new ElementReferenceJsonConverter(
                    new WebElementReferenceContext(this)));
        }

        public void AttachToWebView(IpcSender ipcSender)
        {
            _ipcSender = ipcSender;
        }

        public JsonSerializerOptions ReadJsonSerializerOptions() => JsonSerializerOptions;

        protected override void BeginInvokeJS(long taskId, string identifier, string argsJson, JSCallResultType resultType, long targetInstanceId)
        {
            _ipcSender.BeginInvokeJS(taskId, identifier, argsJson, resultType, targetInstanceId);
        }

        protected override void EndInvokeDotNet(DotNetInvocationInfo invocationInfo, in DotNetInvocationResult invocationResult)
        {
            var resultJsonOrErrorMessage = invocationResult.Success
                ? invocationResult.ResultJson
                : invocationResult.Exception.ToString();
            _ipcSender.EndInvokeDotNet(invocationInfo.CallId, invocationResult.Success, resultJsonOrErrorMessage);
        }
    }
}
