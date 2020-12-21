using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;

namespace RemoteBlazorWebView.Wpf
{
    public class ViewModel : INotifyPropertyChanged
    {
        private string _uri = "";
        public string Uri
        {
            get { return _uri; }
            set
            {
                _uri = value;
                NotifyPropertyChanged("Uri");
            }
        }
        private string _showHyperlink = "Visible";

        public string ShowWebWindow => _showHyperlink == "Visible" ? "Hidden" : "Visible";
       
        public string ShowHyperlink { get { return _showHyperlink; } set {
                _showHyperlink = value;
                NotifyPropertyChanged("ShowHyperlink");
                NotifyPropertyChanged("ShowWebWindow");
            } }

        public event PropertyChangedEventHandler? PropertyChanged;

        public void NotifyPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
