﻿using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace BlazorWebViewTutorial.WpfApp
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private void Application_Startup(object sender, StartupEventArgs e)
        {
            MainWindow wnd = new MainWindow();

            try
            {
                if (e.Args.Length == 1)
                    wnd.Uri = new Uri(e.Args[0]);
            }
            catch (Exception) { }
          

            wnd.Show();
        }
    }
}
