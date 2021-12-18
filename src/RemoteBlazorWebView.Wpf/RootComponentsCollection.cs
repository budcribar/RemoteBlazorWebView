// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.ObjectModel;
using Microsoft.AspNetCore.Components.Web;

namespace PeakSWC.RemoteBlazorWebView.Wpf
{
	public class RootComponentsCollection : ObservableCollection<RootComponent>, IJSComponentConfiguration
	{
		public JSComponentConfigurationStore JSComponents { get; } = new();
	}
}
