using Microsoft.AspNetCore.Components;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PeakSwc.RemoteableWebWindows
{
    public interface IWebViewManager : IDisposable
    {
        void Navigate(string url);
        Task AddRootComponentAsync(Type componentType, string selector, ParameterView parameters);
        Task RemoveRootComponentAsync(string selector);
    }
}
