﻿using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PeakSWC.RemoteWebView;
using Photino.Blazor;
using PhotinoNET;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace PeakSWC.RemoteWebView
{
    public class RemotePhotinoBlazorApp
    {
        public IServiceProvider? Services { get; private set; }

        /// <summary>
        /// Gets configuration for the root components in the window.
        /// </summary>
        public BlazorWindowRootComponents? RootComponents { get; private set; } 
        
        internal void Initialize(IServiceProvider services, RootComponentList rootComponents, Uri serverUrl, Guid id)
        {
            Services = services;
           
            MainWindow = new RemoteBlazorWebViewWindow();
            MainWindow.SetTitle("Photino.Blazor App");
            MainWindow.SetUseOsDefaultLocation(false);
            MainWindow.SetWidth(1000);
            MainWindow.SetHeight(900);
            MainWindow.SetLeft(450);
            MainWindow.SetTop(100);

            MainWindow.ServerUri = serverUrl;
            MainWindow.GrpcBaseUri = MainWindow.GetGrpcBaseUriAsync(serverUrl).Result;
            MainWindow.Id = id;

            MainWindow.RegisterCustomSchemeHandler(PhotinoWebViewManager.BlazorAppScheme, HandleWebRequest);

            // We assume the host page is always in the root of the content directory, because it's
            // unclear there's any other use case. We can add more options later if so.
            string hostPage = @"wwwroot\index.html";
            var contentRootDir = Path.GetDirectoryName(Path.GetFullPath(hostPage))!;
            var hostPageRelativePath = Path.GetRelativePath(contentRootDir, hostPage);
            //var fileProvider = new PhysicalFileProvider(contentRootDir);

            var fileProvider = RemoteWebView.CreateFileProvider(contentRootDir, hostPage);

            var dispatcher = new RemotePhotinoDispatcher(MainWindow);
            var jsComponents = new JSComponentConfigurationStore();

            if (serverUrl == null)
                WindowManager = new PhotinoWebViewManager(MainWindow, services, dispatcher, new Uri(PhotinoWebViewManager.AppBaseUri), fileProvider, jsComponents, hostPageRelativePath);
            else
                WindowManager = new RemotePhotinoWebViewManager(MainWindow, services, dispatcher, new Uri(PhotinoWebViewManager.AppBaseUri), fileProvider, jsComponents, hostPageRelativePath, NullLogger<RemoteBlazorWebViewWindow>.Instance);
            
            RootComponents = new BlazorWindowRootComponents(WindowManager, jsComponents);
            foreach (var component in rootComponents)
            {
                RootComponents.Add(component.Item1, component.Item2);
            }
        }

        public RemoteBlazorWebViewWindow? MainWindow { get; private set; }

        public PhotinoWebViewManager? WindowManager { get; private set; }

        public void Run()
        {
            if(MainWindow?.ServerUri == null)
                WindowManager?.Navigate("/");
            else
            {
                WindowManager?.Navigate(@"wwwroot\index.html");
                MainWindow.NavigateToString("<br/>");
            }
               

            MainWindow?.WaitForClose();
        }

        public Stream HandleWebRequest(object sender, string scheme, string url, out string contentType)
                => WindowManager!.HandleWebRequest(sender, scheme, url, out contentType!)!;

    }
}
