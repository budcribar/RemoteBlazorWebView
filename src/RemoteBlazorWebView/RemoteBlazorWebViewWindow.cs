﻿using Microsoft.AspNetCore.Components;
using PeakSWC.RemoteWebView;
using PhotinoNET;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PeakSWC.RemoteWebView
{
    public class RemoteBlazorWebViewWindow : PhotinoWindow, IBlazorWebView
    {
        public Uri? ServerUri { get; set; }
        public string Group { get; set; } = "test";
        public string Markup { get; set; } = "";

        private Guid id = Guid.Empty;
        public new Guid Id
        {
            get
            {
                if (id == Guid.Empty)
                    throw new Exception("Id not initialized");

                return id;
            }
            set
            {
                if (value == Guid.Empty)
                    id = Guid.NewGuid();
                else
                    id = value;
            }
        }

        public event EventHandler<ConnectedEventArgs>? Connected;
        public event EventHandler<DisconnectedEventArgs>? Disconnected;
        public event EventHandler<RefreshedEventArgs>? Refreshed;
        public event EventHandler<ReadyToConnectEventArgs>? ReadyToConnect;

        public void FireConnected(ConnectedEventArgs args)
        {
            Invoke(() => Connected?.Invoke(this, args));
        }

        public void FireDisconnected(DisconnectedEventArgs args)
        {
            Invoke(() => Disconnected?.Invoke(this, args));
        }

        public void FireRefreshed(RefreshedEventArgs args)
        {
            Invoke(() => Refreshed?.Invoke(this, args));
        }

        public void FireReadyToConnect(ReadyToConnectEventArgs args)
        {
            Invoke(() => ReadyToConnect?.Invoke(this, args));
        }

        public void Restart()
        {
            RemoteWebView.Restart(this);
        }

        public void NavigateToString(string htmlContent)
        {
            // TODO Need to wait for window????
             this.LoadRawString(htmlContent);
        }

        public async Task WaitForInitialitionComplete()
        {
            while(true)
            {
                try
                {
                    var h = this.WindowHandle;
                    await Task.Delay(5000);
                    break;
                }
                catch (Exception)
                {
                    await Task.Delay(100);
                }
            }
           
        }
    }
}
