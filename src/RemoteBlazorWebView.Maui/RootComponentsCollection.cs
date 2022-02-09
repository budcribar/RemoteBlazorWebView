using System.Collections.ObjectModel;
using Microsoft.AspNetCore.Components.Web;

namespace PeakSWC.RemoteBlazorWebView.Maui
{
	public class RootComponentsCollection : ObservableCollection<RootComponent>, IJSComponentConfiguration
	{
		private readonly JSComponentConfigurationStore _jSComponents;

		public RootComponentsCollection(JSComponentConfigurationStore jSComponents)
		{
			_jSComponents = jSComponents;
		}

		public JSComponentConfigurationStore JSComponents => _jSComponents;
	}
}
