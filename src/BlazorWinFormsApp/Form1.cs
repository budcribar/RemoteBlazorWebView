// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.Extensions.DependencyInjection;
using RemoteBlazorWebViewTutorial.Shared;
using System;
using System.Net.Http;
using System.Windows.Forms;
using PeakSWC.RemoteBlazorWebView.WindowsForms;

namespace BlazorWinFormsApp
{
    public partial class Form1 : Form
    {

        public Form1()
        {
            var serviceCollection = new ServiceCollection();
            serviceCollection.AddBlazorWebView();
            serviceCollection.AddScoped<HttpClient>();
            InitializeComponent();

            var runString = new RunString();
            blazorWebView1.ServerUri = runString.ServerUri;
            blazorWebView1.Id = runString.Id;
            blazorWebView1.IsRestarting = runString.IsRestarting;
            blazorWebView1.HostPage = @"wwwroot\index.html";
            blazorWebView1.Services = serviceCollection.BuildServiceProvider();
            blazorWebView1.RootComponents.Add<App>("#app");
            if (runString.ServerUri == null)
            {
                blazorWebView1.Visible = true;
                linkLabel1.Visible = false;
            }
            else
            {
                linkLabel1.Visible = !blazorWebView1.IsRestarting;
                linkLabel1.Text = $"{blazorWebView1.ServerUri}app/{blazorWebView1.Id}";
            }
            blazorWebView1.Unloaded += BlazorWebView1_Unloaded;
        }

        private void BlazorWebView1_Unloaded(object? sender, string e)
        {
            blazorWebView1.BeginInvoke((Action)(() =>
            {
                blazorWebView1.Restart();
                Close();
            }));

        }

        private void LinkLabel1_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            linkLabel1.Visible = false;
            blazorWebView1.StartBrowser();
        }

    }
}
