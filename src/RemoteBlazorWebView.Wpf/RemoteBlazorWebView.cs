using BlazorWebView;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace RemoteBlazorWebView.Wpf
{
    /// <summary>
    /// Follow steps 1a or 1b and then 2 to use this custom control in a XAML file.
    ///
    /// Step 1a) Using this custom control in a XAML file that exists in the current project.
    /// Add this XmlNamespace attribute to the root element of the markup file where it is 
    /// to be used:
    ///
    ///     xmlns:MyNamespace="clr-namespace:RemoteBlazorWebView.Wpf"
    ///
    ///
    /// Step 1b) Using this custom control in a XAML file that exists in a different project.
    /// Add this XmlNamespace attribute to the root element of the markup file where it is 
    /// to be used:
    ///
    ///     xmlns:MyNamespace="clr-namespace:RemoteBlazorWebView.Wpf;assembly=RemoteBlazorWebView.Wpf"
    ///
    /// You will also need to add a project reference from the project where the XAML file lives
    /// to this project and Rebuild to avoid compilation errors:
    ///
    ///     Right click on the target project in the Solution Explorer and
    ///     "Add Reference"->"Projects"->[Select this project]
    ///
    ///
    /// Step 2)
    /// Go ahead and use your control in the XAML file.
    ///
    ///     <MyNamespace:CustomControl1/>
    ///
    /// </summary>
    public class RemoteBlazorWebView : UserControl, IBlazorWebView
    {
        static RemoteBlazorWebView()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(RemoteBlazorWebView), new FrameworkPropertyMetadata(typeof(RemoteBlazorWebView)));
        }

        public event EventHandler<string> OnWebMessageReceived;

        public void Initialize(Action<WebViewOptions> configure)
        {
            throw new NotImplementedException();
        }

        public void Invoke(Action callback)
        {
            throw new NotImplementedException();
        }

        public void NavigateToUrl(string url)
        {
            throw new NotImplementedException();
        }

        public void SendMessage(string message)
        {
            throw new NotImplementedException();
        }

        public void ShowMessage(string title, string message)
        {
            throw new NotImplementedException();
        }
    }
}
