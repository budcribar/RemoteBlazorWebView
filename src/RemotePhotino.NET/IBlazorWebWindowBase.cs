using Microsoft.JSInterop;
using System;
using System.Threading.Tasks;

namespace PeakSWC.RemoteBlazorWebView.Windows
{
    public interface IDispatcher
    {
        public Task InvokeAsync(Action workItem);
    }

    public interface IBlazorWebWindowBase
    {
        void WaitForClose();
        IDispatcher? PlatformDispatcher { get; set; }
        IJSRuntime? JSRuntime { get; set; }
        event EventHandler<string> WebMessageReceived;
        IBlazorWebWindowBase LoadBase(string path);
        IBlazorWebWindowBase OpenAlertWindowBase(string title, string message);
        IBlazorWebWindowBase SendWebMessageBase(string message);
    }
}