﻿using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
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

        public  IFileProvider CreateFileProvider(string contentRootDir, string hostPage)
        {
            IFileProvider? provider = null;
            var root = Path.GetDirectoryName(hostPage) ?? string.Empty;
            var entryAssembly = Assembly.GetEntryAssembly()!;
            
            try
            {
                EmbeddedFilesManifest manifest = ManifestParser.Parse(entryAssembly);
                var dir = manifest._rootDirectory.Children.Where(x => x is ManifestDirectory md && md.Children.Any(y => y.Name == root)).FirstOrDefault();

                if (dir != null)
                {
                    var manifestRoot = Path.Combine(dir.Name, root);
                    provider = new ManifestEmbeddedFileProvider(entryAssembly, manifestRoot);
                }
                else throw new Exception("Try fixed manifest");
            }
            catch (Exception)
            {
                try
                {
                    EmbeddedFilesManifest manifest = ManifestParser.Parse(new FixedManifestEmbeddedAssembly(entryAssembly));
                    var dir = manifest._rootDirectory.Children.Where(x => x is ManifestDirectory md && md.Children.Any(y => y.Name == root)).FirstOrDefault();

                    if (dir != null)
                    {
                        var manifestRoot = Path.Combine(dir.Name, root);
                        provider = new ManifestEmbeddedFileProvider(new FixedManifestEmbeddedAssembly(entryAssembly), manifestRoot);
                    }
                }
                catch (Exception) {  }
            }

            if (provider == null)
                provider = new PhysicalFileProvider(contentRootDir);

            return provider;
        }
        
        internal void Initialize(IServiceProvider services, RootComponentList rootComponents, Uri serverUrl, Guid id, bool isRestarting)
        {
            Services = services;
           
            MainWindow = new RemoteBlazorWebViewWindow();
            MainWindow.SetTitle("Photino.Blazor App");
            MainWindow.SetUseOsDefaultLocation(false);
            MainWindow.SetWidth(1000);
            MainWindow.SetHeight(900);
            MainWindow.SetLeft(450);
            MainWindow.SetTop(100);

            //MainWindow.ServerUri = new Uri("https://localhost:5001");
            MainWindow.ServerUri = serverUrl;
            MainWindow.Id = id;
            MainWindow.IsRestarting = isRestarting;

            MainWindow.RegisterCustomSchemeHandler(PhotinoWebViewManager.BlazorAppScheme, HandleWebRequest);

            // We assume the host page is always in the root of the content directory, because it's
            // unclear there's any other use case. We can add more options later if so.
            string hostPage = @"wwwroot\index.html";
            var contentRootDir = Path.GetDirectoryName(Path.GetFullPath(hostPage))!;
            var hostPageRelativePath = Path.GetRelativePath(contentRootDir, hostPage);
            //var fileProvider = new PhysicalFileProvider(contentRootDir);

            var fileProvider = CreateFileProvider(contentRootDir, hostPage);


            var dispatcher = new RemotePhotinoDispatcher(MainWindow);
            var jsComponents = new JSComponentConfigurationStore();

            if (serverUrl == null)
                WindowManager = new PhotinoWebViewManager(MainWindow, services, dispatcher, new Uri(PhotinoWebViewManager.AppBaseUri), fileProvider, jsComponents, hostPageRelativePath);
            else
                WindowManager = new RemotePhotinoWebViewManager(MainWindow, services, dispatcher, new Uri(PhotinoWebViewManager.AppBaseUri), fileProvider, jsComponents, hostPageRelativePath);
            
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
                WindowManager?.Navigate(@"wwwroot\index.html");

            MainWindow?.WaitForClose();
        }

        public Stream HandleWebRequest(object sender, string scheme, string url, out string contentType)
                => WindowManager!.HandleWebRequest(sender, scheme, url, out contentType!)!;

    }
}
