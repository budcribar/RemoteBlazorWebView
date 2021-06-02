using Microsoft.JSInterop;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;

namespace PhotinoNET
{
    public interface IDispatcher
    {
        public Task InvokeAsync(Action workItem);
    }

    public interface IPhotinoWindowBase
    {
        void WaitForClose();
        IDispatcher PlatformDispatcher { get; set; }
        IJSRuntime? JSRuntime { get; set; }
        event EventHandler<string> WebMessageReceived;
        IPhotinoWindowBase LoadBase(string path);
        IPhotinoWindowBase OpenAlertWindowBase(string title, string message);
        IPhotinoWindowBase SendWebMessageBase(string message);
    }
}