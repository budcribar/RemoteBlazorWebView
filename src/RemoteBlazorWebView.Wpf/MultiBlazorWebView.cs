using BlazorWebView;
using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;

namespace RemoteBlazorWebView.Wpf
{

    public class MultiBlazorWebView : IBlazorWebView
    {
        private List<(IBlazorWebView view, bool controller)> blazorWebViews = new();

        public void Add(IBlazorWebView view, bool controller)
        {
            blazorWebViews.Add((view, controller));
        }

        public event EventHandler<string>? OnWebMessageReceived;

        public void Initialize(Action<WebViewOptions> configure)
        {
            blazorWebViews.ForEach(x => x.view.Initialize(configure));
        }

        public void Invoke(Action callback)
        {
            blazorWebViews.ForEach(x => x.view.Invoke(callback));
        }

        public void NavigateToUrl(string url)
        {
            blazorWebViews.ForEach(x => x.view.NavigateToUrl(url));
        }

        public void SendMessage(string message)
        {
            blazorWebViews.Where(x => x.controller).First().view.SendMessage(message);
        }

        public void ShowMessage(string title, string message)
        {
            throw new NotImplementedException();
        }
    }
}
