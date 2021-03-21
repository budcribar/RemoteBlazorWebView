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

        public event EventHandler<string>? OnWebMessageReceived
        {
            add
            {
                blazorWebViews.ForEach(x => x.view.OnWebMessageReceived += value);
            }

            remove
            {
                blazorWebViews.ForEach(x => x.view.OnWebMessageReceived -= value);
            }
        }

        public void Initialize(Action<WebViewOptions> configure)
        {
            //blazorWebViews.Where(x => x.controller).First().view.Initialize(configure);
            blazorWebViews.ForEach(x => x.view.Initialize(configure));
        }

        public void Invoke(Action callback)
        {
            blazorWebViews.ForEach(x => x.view.Invoke(callback));
        }

        public void NavigateToUrl(string url)
        {
            //blazorWebViews.Where(x => x.controller).First().view.NavigateToUrl(url);
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
