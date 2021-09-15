using Microsoft.AspNetCore.Components;
using System;
using System.Threading.Tasks;

namespace PeakSWC.RemoteableWebView
{
    public interface IWebViewManager : IAsyncDisposable
    {
        void Navigate(string url);
        Task AddRootComponentAsync(Type componentType, string selector, ParameterView parameters);
        Task RemoveRootComponentAsync(string selector);
    }
}
