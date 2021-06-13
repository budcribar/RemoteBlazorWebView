// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.Net.Http;
using System.Windows.Forms;
using Microsoft.AspNetCore.Components.WebView.WindowsForms;
using Microsoft.Extensions.DependencyInjection;
using RemoteBlazorWebViewTutorial.Shared;

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
                //blazorWebView1.Visible = false;
                linkLabel1.Visible = true;
                linkLabel1.Text = $"{blazorWebView1.ServerUri}app/{blazorWebView1.Id}"; 
            }
        }

        private void linkLabel1_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            var url = $"{blazorWebView1.ServerUri}app/{blazorWebView1.Id}";

            try
            {
                linkLabel1.Visible = false;
                Process.Start(new ProcessStartInfo("cmd", $"/c start microsoft-edge:" + url) { CreateNoWindow = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Unable to open hyperlink. Error:{ex.Message}");
            }
        }
        
    }
}
