using System;
using Microsoft.AspNetCore.Components;
using System.Threading.Tasks;
using Microsoft.Maui.Controls;

namespace PeakSWC.RemoteBlazorWebView.Maui
{
	internal sealed class MauiDispatcher : Dispatcher
	{
#pragma warning disable CA1416 // Validate platform compatibility
		public override bool CheckAccess()
		{
			return !Device.IsInvokeRequired;
		}

		public override Task InvokeAsync(Action workItem)
		{
			return Device.InvokeOnMainThreadAsync(workItem);
		}

		public override Task InvokeAsync(Func<Task> workItem)
		{
			return Device.InvokeOnMainThreadAsync(workItem);
		}

		public override Task<TResult> InvokeAsync<TResult>(Func<TResult> workItem)
		{
			return Device.InvokeOnMainThreadAsync(workItem);
		}

		public override Task<TResult> InvokeAsync<TResult>(Func<Task<TResult>> workItem)
		{
			return Device.InvokeOnMainThreadAsync(workItem);
		}
#pragma warning restore CA1416 // Validate platform compatibility
	}
}
